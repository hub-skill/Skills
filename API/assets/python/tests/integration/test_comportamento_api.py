"""Comportamentos genéricos da API, válidos para qualquer API gerada pela skill:
formato dos erros, limites, timeout, health, cabeçalhos, métricas e documentação.
Ao adaptar o template, troque só URL e CORPO_VALIDO (e a dependência do serviço, se mudar de nome).
"""

import importlib
import os
import time
from collections.abc import AsyncIterator, Iterator

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

import app.main as modulo_main
from app.core.config import get_settings
from app.core.limiter import limiter
from app.main import app
from app.services.transformacao import get_transformador

URL = "/api/v1/transformar"
CORPO_VALIDO = {"texto": "ola"}
CABECALHOS = {"X-API-Key": os.environ["API_KEY"]}


@pytest_asyncio.fixture
async def cliente() -> AsyncIterator[AsyncClient]:
    # raise_app_exceptions=False: o teste do 500 precisa ver a resposta, não a exceção.
    transporte = ASGITransport(app=app, raise_app_exceptions=False)
    async with AsyncClient(transport=transporte, base_url="http://test") as cliente_http:
        yield cliente_http
    app.dependency_overrides.clear()


async def test_erro_401_tem_formato_padrao_e_www_authenticate(cliente: AsyncClient) -> None:
    resposta = await cliente.post(URL, json=CORPO_VALIDO)
    assert resposta.status_code == 401
    assert resposta.headers["www-authenticate"] == "APIKey"
    erro = resposta.json()["error"]
    assert erro["code"] == "http_401" and erro["request_id"]


async def test_rota_inexistente_responde_404_no_formato_padrao(cliente: AsyncClient) -> None:
    resposta = await cliente.get("/nao-existe", headers=CABECALHOS)
    assert resposta.status_code == 404
    assert resposta.json()["error"]["code"] == "http_404"


async def test_corpo_grande_demais_responde_413(cliente: AsyncClient) -> None:
    corpo = b'{"texto": "' + b"a" * 70_000 + b'"}'
    resposta = await cliente.post(
        URL, content=corpo, headers={**CABECALHOS, "Content-Type": "application/json"}
    )
    assert resposta.status_code == 413
    assert resposta.json()["error"]["code"] == "corpo_muito_grande"


async def test_json_invalido_responde_400_e_nao_500(cliente: AsyncClient) -> None:
    resposta = await cliente.post(
        URL, content=b"{nao e json", headers={**CABECALHOS, "Content-Type": "application/json"}
    )
    assert resposta.status_code == 400
    assert resposta.json()["error"]["code"] == "requisicao_invalida"


async def test_health_e_cabecalhos_de_seguranca(cliente: AsyncClient) -> None:
    vivo = await cliente.get("/health/live")
    pronto = await cliente.get("/health/ready")
    assert vivo.status_code == 200 and pronto.status_code == 200
    assert vivo.headers["x-content-type-options"] == "nosniff"
    assert vivo.headers["x-frame-options"] == "DENY"
    assert vivo.headers["strict-transport-security"].startswith("max-age=")
    assert vivo.headers["x-request-id"]


async def test_metricas_prometheus(cliente: AsyncClient) -> None:
    await cliente.post(URL, json=CORPO_VALIDO, headers=CABECALHOS)
    resposta = await cliente.get("/metrics")
    assert resposta.status_code == 200
    assert "http_request" in resposta.text


@pytest.fixture
def limite_baixo(monkeypatch: pytest.MonkeyPatch) -> Iterator[None]:
    # O slowapi lê o limite a cada requisição (lambda em routes.py): basta trocar a configuração.
    monkeypatch.setattr(get_settings(), "rate_limit", "2/minute")
    limiter.reset()
    yield
    limiter.reset()


@pytest.mark.usefixtures("limite_baixo")
async def test_rate_limit_responde_429_com_retry_after(cliente: AsyncClient) -> None:
    for _ in range(2):
        assert (await cliente.post(URL, json=CORPO_VALIDO, headers=CABECALHOS)).status_code == 200

    bloqueada = await cliente.post(URL, json=CORPO_VALIDO, headers=CABECALHOS)
    assert bloqueada.status_code == 429
    assert bloqueada.headers["retry-after"] == "60"
    assert bloqueada.json()["error"]["code"] == "limite_excedido"


async def test_processamento_lento_responde_504(cliente: AsyncClient) -> None:
    def lento(texto: str) -> str:
        time.sleep(3)  # REQUEST_TIMEOUT_SECONDS=1 no conftest
        return texto

    app.dependency_overrides[get_transformador] = lambda: lento
    resposta = await cliente.post(URL, json=CORPO_VALIDO, headers=CABECALHOS)
    assert resposta.status_code == 504
    assert resposta.json()["error"]["code"] == "timeout"


async def test_erro_inesperado_responde_500_generico_sem_vazar_detalhes(cliente: AsyncClient) -> None:
    def quebra(texto: str) -> str:
        raise RuntimeError("detalhe interno: senha=abc")

    app.dependency_overrides[get_transformador] = lambda: quebra
    resposta = await cliente.post(URL, json=CORPO_VALIDO, headers=CABECALHOS)
    assert resposta.status_code == 500
    erro = resposta.json()["error"]
    assert erro["code"] == "erro_interno" and erro["request_id"]
    assert "detalhe interno" not in resposta.text
    assert "RuntimeError" not in resposta.text


async def test_documentacao_desligada_por_padrao(cliente: AsyncClient) -> None:
    for rota in ("/docs", "/redoc", "/openapi.json"):
        assert (await cliente.get(rota)).status_code == 404


async def test_documentacao_ligada_e_publica(monkeypatch: pytest.MonkeyPatch) -> None:
    # A configuração é lida na importação: recarrega o módulo com ENABLE_DOCS=true e depois restaura.
    monkeypatch.setenv("ENABLE_DOCS", "true")
    get_settings.cache_clear()
    try:
        ligada = importlib.reload(modulo_main).app
        async with AsyncClient(transport=ASGITransport(app=ligada), base_url="http://test") as cliente:
            openapi = await cliente.get("/openapi.json")
            assert openapi.status_code == 200 and URL in openapi.text
            assert (await cliente.get("/docs")).status_code == 200
    finally:
        monkeypatch.setenv("ENABLE_DOCS", "false")
        get_settings.cache_clear()
        importlib.reload(modulo_main)
