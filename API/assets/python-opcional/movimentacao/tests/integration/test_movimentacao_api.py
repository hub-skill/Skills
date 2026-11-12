"""Movimentação ligada à API do template: numeração na chegada, resposta, X-Requisicao e log de erros.

Sem banco: um repositório falso numera e guarda tudo. Para ver o que foi gravado, os testes chamam
``movimentacao.parar()``, que grava o que ainda está nas filas no repositório falso.
"""

import asyncio
import os
from collections.abc import AsyncIterator, Iterator

import anyio
import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

from app.core.config import get_settings
from app.core.limiter import limiter
from app.main import app
from app.movimentacao.http import Movimentacao
from app.movimentacao.registros import RegistroErro, RegistroResposta
from app.services.transformacao import get_transformador
from tests.contrato import afirmar_problema

URL = "/api/v1/transformar"
CABECALHOS = {"X-API-Key": os.environ["API_KEY"]}
CABECALHOS_JSON = {**CABECALHOS, "Content-Type": "application/json"}
ANO = 2026


class RepositorioFalso:
    def __init__(self) -> None:
        self.chegadas: list[tuple[str, str, str | None]] = []  # (método, rota, conteúdo); número = posição + 1
        self.respostas: dict[int, RegistroResposta] = {}
        self.erros: list[RegistroErro] = []
        self.numeracao_falha = False
        self.numeracao_atraso = 0.0

    async def registrar_requisicao(self, metodo: str, rota: str, conteudo: str | None) -> tuple[int, int]:
        if self.numeracao_falha:
            raise ConnectionError("banco fora")
        await anyio.sleep(self.numeracao_atraso)
        self.chegadas.append((metodo, rota, conteudo))
        return ANO, len(self.chegadas)

    async def registrar_respostas(self, registros: list[RegistroResposta]) -> None:
        for r in registros:
            self.respostas[r.requisicao] = r

    async def gravar_erros(self, registros: list[RegistroErro]) -> None:
        self.erros.extend(registros)


@pytest.fixture
def repo() -> RepositorioFalso:
    return RepositorioFalso()


@pytest.fixture
def prazo() -> float:
    return 0.3  # curto, para o teste do banco lento não demorar


@pytest_asyncio.fixture
async def movimentacao(repo: RepositorioFalso, prazo: float) -> AsyncIterator[Movimentacao]:
    app.state.movimentacao = Movimentacao(repo, ambiente="teste", prazo_para_numerar=prazo)
    yield app.state.movimentacao
    app.dependency_overrides.clear()
    del app.state.movimentacao


@pytest_asyncio.fixture
async def cliente(movimentacao: Movimentacao) -> AsyncIterator[AsyncClient]:
    async with AsyncClient(transport=ASGITransport(app=app, raise_app_exceptions=False), base_url="http://test") as c:
        yield c


async def test_requisicao_com_sucesso_e_numerada_e_a_resposta_e_gravada(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    resposta = await cliente.post(URL, json={"texto": "  ola  "}, headers=CABECALHOS)
    await movimentacao.parar()

    assert resposta.status_code == 200
    assert resposta.headers["x-requisicao"] == f"{ANO}-000001"
    metodo, rota, conteudo = repo.chegadas[0]
    assert (metodo, rota) == ("POST", URL)
    assert conteudo is not None and "ola" in conteudo
    gravada = repo.respostas[1]
    assert (gravada.ano, gravada.status_http) == (ANO, 200)
    assert (gravada.situacao_requisicao, gravada.situacao_resposta) == ("OK", "OK")
    assert gravada.conteudo == resposta.text  # o conteúdo gravado é exatamente o que o cliente recebeu


async def test_cpf_e_mascarado_na_requisicao_e_na_resposta(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    resposta = await cliente.post(URL, json={"texto": "cpf 529.982.247-25"}, headers=CABECALHOS)
    await movimentacao.parar()

    assert resposta.status_code == 200
    conteudo = repo.chegadas[0][2]
    assert conteudo is not None and "***.982.247-**" in conteudo and "529.982.247-25" not in conteudo
    gravada = repo.respostas[1].conteudo
    assert gravada is not None and "***.982.247-**" in gravada and "529.982.247-25" not in gravada


async def test_conteudo_da_requisicao_aceita_qualquer_formato(cliente: AsyncClient, repo: RepositorioFalso) -> None:
    for corpo in (b"{nao e json", b"texto solto \xff com byte invalido", b'["lista"]', b"<xml><a>1</a></xml>"):
        await cliente.post(URL, content=corpo, headers=CABECALHOS_JSON)

    gravados = [c for _, _, c in repo.chegadas]
    assert gravados[0] == "{nao e json"
    assert gravados[1] is not None and gravados[1].startswith("texto solto ") and "�" in gravados[1]
    assert gravados[2:] == ['["lista"]', "<xml><a>1</a></xml>"]


@pytest.mark.parametrize(
    ("chamada", "status", "situacao_requisicao", "situacao_resposta"),
    [
        ("entrada_invalida", 422, "ER", "ER"),
        ("rota_inexistente", 404, "ER", "ER"),
        ("json_malformado", 400, "ER", "ER"),
        ("erro_inesperado", 500, "OK", "ER"),
        ("sucesso", 200, "OK", "OK"),
    ],
)
async def test_situacoes_da_requisicao_e_da_resposta(
    cliente: AsyncClient,
    repo: RepositorioFalso,
    movimentacao: Movimentacao,
    chamada: str,
    status: int,
    situacao_requisicao: str,
    situacao_resposta: str,
) -> None:
    if chamada == "entrada_invalida":
        resposta = await cliente.post(URL, json={"texto": "   "}, headers=CABECALHOS)
    elif chamada == "rota_inexistente":
        resposta = await cliente.get("/nao-existe", headers=CABECALHOS)
    elif chamada == "json_malformado":
        resposta = await cliente.post(URL, content=b"{nao e json", headers=CABECALHOS_JSON)
    elif chamada == "erro_inesperado":

        def quebra(texto: str) -> str:
            raise RuntimeError("falha")

        app.dependency_overrides[get_transformador] = lambda: quebra
        resposta = await cliente.post(URL, json={"texto": "ola"}, headers=CABECALHOS)
    else:
        resposta = await cliente.post(URL, json={"texto": "ola"}, headers=CABECALHOS)
    await movimentacao.parar()

    assert resposta.status_code == status
    assert resposta.headers["x-requisicao"] == f"{ANO}-000001"
    gravada = repo.respostas[1]
    assert (gravada.status_http, gravada.situacao_requisicao, gravada.situacao_resposta) == (
        status,
        situacao_requisicao,
        situacao_resposta,
    )


async def test_erro_nao_tratado_entra_na_movimentacao_com_os_cabecalhos_da_api(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    def quebra(texto: str) -> str:
        raise RuntimeError("detalhe interno: senha=abc")

    app.dependency_overrides[get_transformador] = lambda: quebra
    resposta = await cliente.post(URL, json={"texto": "ola"}, headers=CABECALHOS)
    await movimentacao.parar()

    assert resposta.status_code == 500
    assert resposta.headers["x-requisicao"] and resposta.headers["x-request-id"]
    assert resposta.headers["x-content-type-options"] == "nosniff"
    assert repo.respostas[1].conteudo is not None and "senha" not in repo.respostas[1].conteudo
    assert [(e.status, e.codigo, e.tipo_excecao) for e in repo.erros] == [(500, "erro_interno", "builtins.RuntimeError")]


@pytest.mark.parametrize(
    ("metodo", "rota", "cabecalhos"),
    [
        ("POST", URL, {}),  # sem chave
        ("POST", URL, {"X-API-Key": "chave-errada"}),
        ("GET", "/nao-existe", {}),  # sem chave: 401, não revela se a rota existe
        ("GET", URL, {}),
    ],
    ids=["sem_chave", "chave_errada", "rota_inexistente_sem_chave", "metodo_errado_sem_chave"],
)
async def test_sem_chave_valida_nao_entra_na_movimentacao(
    cliente: AsyncClient,
    repo: RepositorioFalso,
    movimentacao: Movimentacao,
    metodo: str,
    rota: str,
    cabecalhos: dict[str, str],
) -> None:
    # Varreduras e chamadas sem chave recebem 401 e não gravam nada: nem movimentação, nem log de erros.
    resposta = await cliente.request(metodo, rota, json={"texto": "ola"}, headers=cabecalhos)
    await movimentacao.parar()

    afirmar_problema(resposta, 401, "http_401")
    assert "x-requisicao" not in resposta.headers
    assert repo.chegadas == [] and repo.respostas == {} and repo.erros == []


async def test_health_e_metrics_nao_entram_na_movimentacao(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    for rota in ("/health/live", "/health/ready", "/metrics"):
        resposta = await cliente.get(rota)
        assert resposta.status_code == 200
        assert "x-requisicao" not in resposta.headers
    await movimentacao.parar()

    assert repo.chegadas == [] and repo.respostas == {}


async def test_cada_erro_vai_para_o_log_com_a_chave_da_sua_movimentacao(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    primeira = await cliente.post(URL, json={"texto": "   "}, headers=CABECALHOS)
    segunda = await cliente.get("/nao-existe", headers=CABECALHOS)
    await movimentacao.parar()

    assert [(e.ano, e.requisicao, e.status, e.codigo) for e in repo.erros] == [
        (ANO, 1, 422, "entrada_invalida"),
        (ANO, 2, 404, "http_404"),
    ]
    assert primeira.headers["x-requisicao"] == f"{ANO}-000001" and segunda.headers["x-requisicao"] == f"{ANO}-000002"
    erro = repo.erros[0]
    assert erro.request_id == primeira.headers["x-request-id"] and erro.ambiente == "teste"
    assert erro.duracao_ms is not None and erro.duracao_ms >= 0
    assert "texto" not in repr(erro)  # nunca o corpo da requisição


async def test_sucesso_nao_vai_para_o_log_de_erros(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    await cliente.post(URL, json={"texto": "ola"}, headers=CABECALHOS)
    await movimentacao.parar()

    assert repo.erros == []


@pytest.fixture
def limite_baixo(monkeypatch: pytest.MonkeyPatch) -> Iterator[None]:
    monkeypatch.setattr(get_settings(), "rate_limit", "1/minute")
    limiter.reset()
    yield
    limiter.reset()


@pytest.mark.usefixtures("limite_baixo")
async def test_limite_de_requisicoes_estourado_tambem_entra_na_movimentacao(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    await cliente.post(URL, json={"texto": "ola"}, headers=CABECALHOS)
    bloqueada = await cliente.post(URL, json={"texto": "ola"}, headers=CABECALHOS)
    await movimentacao.parar()

    assert bloqueada.status_code == 429 and bloqueada.headers["x-requisicao"] == f"{ANO}-000002"
    assert (repo.respostas[2].situacao_requisicao, repo.respostas[2].situacao_resposta) == ("ER", "ER")
    assert [(e.status, e.codigo) for e in repo.erros] == [(429, "limite_excedido")]


async def test_corpo_acima_do_limite_registra_so_o_tamanho(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    corpo = b'{"texto": "' + b"1" * 70_000 + b'"}'
    resposta = await cliente.post(URL, content=corpo, headers=CABECALHOS_JSON)
    await movimentacao.parar()

    assert resposta.status_code == 413
    conteudo = repo.chegadas[0][2]
    assert conteudo is not None and str(len(corpo)) in conteudo and "1111" not in conteudo
    assert [e.codigo for e in repo.erros] == ["corpo_muito_grande"]


async def test_sem_numero_a_requisicao_segue_e_nada_e_gravado(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    repo.numeracao_falha = True
    ok = await cliente.post(URL, json={"texto": "ola"}, headers=CABECALHOS)
    erro = await cliente.post(URL, json={"texto": "   "}, headers=CABECALHOS)
    await movimentacao.parar()

    assert ok.status_code == 200 and erro.status_code == 422
    assert "x-requisicao" not in ok.headers and "x-requisicao" not in erro.headers
    assert repo.respostas == {} and repo.erros == []  # o log de erros nunca recebe linha sem a chave


async def test_banco_lento_na_chegada_nao_segura_a_requisicao_alem_do_prazo(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    repo.numeracao_atraso = 5.0
    resposta = await cliente.post(URL, json={"texto": "ola"}, headers=CABECALHOS)
    await movimentacao.parar()

    assert resposta.status_code == 200 and "x-requisicao" not in resposta.headers
    assert repo.chegadas == []  # a gravação lenta foi abandonada


async def test_iniciar_grava_em_segundo_plano_e_parar_encerra(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    movimentacao.iniciar()
    await cliente.post(URL, json={"texto": "   "}, headers=CABECALHOS)

    async with asyncio.timeout(2):
        while 1 not in repo.respostas or not repo.erros:  # sem parar(): a tarefa de fundo grava sozinha
            await asyncio.sleep(0.01)
    await movimentacao.parar()

    assert repo.respostas[1].status_http == 422 and repo.erros[0].codigo == "entrada_invalida"
