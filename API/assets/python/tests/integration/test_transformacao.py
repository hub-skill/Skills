import os

# Definido antes de importar a aplicação, porque main.py lê a configuração na importação.
os.environ.setdefault("API_KEY", "chave-de-teste-com-pelo-menos-32-caracteres")

import pytest_asyncio  # noqa: E402
from httpx import ASGITransport, AsyncClient  # noqa: E402
import pytest  # noqa: E402

from app.main import app  # noqa: E402

URL = "/api/v1/transformar"
CABECALHOS = {"X-API-Key": os.environ["API_KEY"]}


@pytest_asyncio.fixture
async def cliente():
    transporte = ASGITransport(app=app)
    async with AsyncClient(transport=transporte, base_url="http://test") as cliente_http:
        yield cliente_http


async def test_transforma_texto_e_remove_espacos(cliente):
    resposta = await cliente.post(URL, json={"texto": "  ola  "}, headers=CABECALHOS)
    assert resposta.status_code == 200
    assert resposta.json() == {"resultado": "OLA"}


async def test_aceita_unicode_e_tamanho_maximo(cliente):
    resposta = await cliente.post(URL, json={"texto": "ç" * 10_000}, headers=CABECALHOS)
    assert resposta.status_code == 200
    assert resposta.json()["resultado"] == "Ç" * 10_000


async def test_rejeita_sem_chave(cliente):
    resposta = await cliente.post(URL, json={"texto": "ola"})
    assert resposta.status_code == 401


async def test_rejeita_chave_errada(cliente):
    resposta = await cliente.post(URL, json={"texto": "ola"}, headers={"X-API-Key": "errada"})
    assert resposta.status_code == 401


@pytest.mark.parametrize(
    "texto",
    ["", "   ", "a" * 10_001, "ola\x00mundo", "ola\x07"],
    ids=["vazio", "so_espacos", "acima_do_maximo", "byte_nulo", "controle"],
)
async def test_rejeita_entrada_invalida(cliente, texto):
    resposta = await cliente.post(URL, json={"texto": texto}, headers=CABECALHOS)
    assert resposta.status_code == 422
    assert resposta.json()["error"]["code"] == "entrada_invalida"
    assert resposta.json()["error"]["request_id"] is not None


async def test_nao_ecoa_entrada_maliciosa_no_erro(cliente):
    payload = "'; DROP TABLE usuarios; --\x00"
    resposta = await cliente.post(URL, json={"texto": payload}, headers=CABECALHOS)
    assert resposta.status_code == 422
    assert payload not in resposta.text
