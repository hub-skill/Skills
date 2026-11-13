"""Movimentação ligada à API do template: ULID na chegada, X-Requisicao, uma gravação por requisição e o erro.

Sem banco: um repositório falso guarda tudo. Para ver o que foi gravado, os testes chamam
``movimentacao.parar()``, que grava o que ainda está na fila no repositório falso.
"""

import asyncio
import os
from collections.abc import AsyncIterator, Iterator
from datetime import UTC, datetime

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

from app.core.config import get_settings
from app.core.limiter import limiter
from app.main import app
from app.movimentacao import ulid
from app.movimentacao.http import Movimentacao
from app.movimentacao.registros import RegistroMovimentacao
from app.services.transformacao import get_transformador
from tests.contrato import afirmar_problema

URL = "/api/v1/transformar"
CABECALHOS = {"X-API-Key": os.environ["API_KEY"]}
CABECALHOS_JSON = {**CABECALHOS, "Content-Type": "application/json"}


class RepositorioFalso:
    def __init__(self) -> None:
        self.registros: list[RegistroMovimentacao] = []
        self.falha = False
        self.portao: asyncio.Event | None = None  # enquanto fechado, a gravação espera (banco lento)

    async def gravar(self, registros: list[RegistroMovimentacao]) -> None:
        if self.falha:
            raise ConnectionError("banco fora")
        if self.portao is not None:
            await self.portao.wait()
        self.registros.extend(registros)

    def por_id(self, requisicao_id: str) -> RegistroMovimentacao:
        return next(r for r in self.registros if r.requisicao_id == requisicao_id)


def id_do_header(resposta: object) -> str:
    identificador: str = resposta.headers["x-requisicao"]  # type: ignore[attr-defined]
    assert ulid.valido(identificador), identificador
    return identificador


@pytest.fixture
def repo() -> RepositorioFalso:
    return RepositorioFalso()


@pytest_asyncio.fixture
async def movimentacao(repo: RepositorioFalso) -> AsyncIterator[Movimentacao]:
    app.state.movimentacao = Movimentacao(repo, ambiente="teste")
    yield app.state.movimentacao
    app.dependency_overrides.clear()
    del app.state.movimentacao


@pytest_asyncio.fixture
async def cliente(movimentacao: Movimentacao) -> AsyncIterator[AsyncClient]:
    async with AsyncClient(
        transport=ASGITransport(app=app, raise_app_exceptions=False), base_url="http://test"
    ) as c:
        yield c


async def test_requisicao_com_sucesso_ganha_ulid_e_e_gravada_com_a_resposta(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    resposta = await cliente.post(URL, json={"texto": "  ola  "}, headers=CABECALHOS)
    await movimentacao.parar()

    assert resposta.status_code == 200
    gravada = repo.por_id(id_do_header(resposta))
    assert (gravada.metodo, gravada.rota, gravada.status_http) == ("POST", URL, 200)
    assert gravada.requisicao_conteudo is not None and "ola" in gravada.requisicao_conteudo
    assert (gravada.situacao_requisicao, gravada.situacao_resposta) == ("OK", "OK")
    assert (
        gravada.resposta_conteudo == resposta.text
    )  # o conteúdo gravado é exatamente o que o cliente recebeu
    assert gravada.erro is None and gravada.resposta_em >= gravada.requisicao_em
    assert (
        abs((gravada.requisicao_em - ulid.momento(gravada.requisicao_id)).total_seconds()) < 0.002
    )  # a hora no ULID


async def test_cpf_e_mascarado_na_requisicao_e_na_resposta(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    resposta = await cliente.post(URL, json={"texto": "cpf 529.982.247-25"}, headers=CABECALHOS)
    await movimentacao.parar()

    gravada = repo.por_id(id_do_header(resposta))
    assert resposta.status_code == 200
    assert gravada.requisicao_conteudo is not None and "***.982.247-**" in gravada.requisicao_conteudo
    assert "529.982.247-25" not in gravada.requisicao_conteudo
    assert gravada.resposta_conteudo is not None and "***.982.247-**" in gravada.resposta_conteudo
    assert "529.982.247-25" not in gravada.resposta_conteudo


async def test_conteudo_da_requisicao_aceita_qualquer_formato(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    corpos = (b"{nao e json", b"texto solto \xff com byte invalido", b'["lista"]', b"<xml><a>1</a></xml>")
    ids = [id_do_header(await cliente.post(URL, content=corpo, headers=CABECALHOS_JSON)) for corpo in corpos]
    await movimentacao.parar()

    gravados = [repo.por_id(i).requisicao_conteudo for i in ids]
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
    gravada = repo.por_id(id_do_header(resposta))
    assert (gravada.status_http, gravada.situacao_requisicao, gravada.situacao_resposta) == (
        status,
        situacao_requisicao,
        situacao_resposta,
    )


async def test_erro_nao_tratado_entra_na_movimentacao_com_os_cabecalhos_da_api_e_o_erro_junto(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    def quebra(texto: str) -> str:
        raise RuntimeError("detalhe interno: senha=abc")

    app.dependency_overrides[get_transformador] = lambda: quebra
    resposta = await cliente.post(URL, json={"texto": "ola"}, headers=CABECALHOS)
    await movimentacao.parar()

    assert resposta.status_code == 500
    assert resposta.headers["x-request-id"] and resposta.headers["x-content-type-options"] == "nosniff"
    gravada = repo.por_id(id_do_header(resposta))
    assert gravada.resposta_conteudo is not None and "senha" not in gravada.resposta_conteudo
    assert gravada.erro is not None
    assert (gravada.erro.status, gravada.erro.codigo, gravada.erro.tipo_excecao) == (
        500,
        "erro_interno",
        "builtins.RuntimeError",
    )


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
    # Varreduras e chamadas sem chave recebem 401 e não gravam nada.
    resposta = await cliente.request(metodo, rota, json={"texto": "ola"}, headers=cabecalhos)
    await movimentacao.parar()

    afirmar_problema(resposta, 401, "http_401")
    assert "x-requisicao" not in resposta.headers
    assert repo.registros == []


async def test_health_e_metrics_nao_entram_na_movimentacao(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    for rota in ("/health/live", "/health/ready", "/metrics"):
        resposta = await cliente.get(rota)
        assert resposta.status_code == 200
        assert "x-requisicao" not in resposta.headers
    await movimentacao.parar()

    assert repo.registros == []


async def test_cada_erro_e_gravado_junto_com_a_sua_movimentacao_sem_o_corpo(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    primeira = await cliente.post(URL, json={"texto": "   "}, headers=CABECALHOS)
    segunda = await cliente.get("/nao-existe", headers=CABECALHOS)
    await movimentacao.parar()

    for resposta, codigo in ((primeira, "entrada_invalida"), (segunda, "http_404")):
        erro = repo.por_id(id_do_header(resposta)).erro
        assert erro is not None and erro.codigo == codigo
        assert erro.request_id == resposta.headers["x-request-id"] and erro.ambiente == "teste"
        assert erro.duracao_ms is not None and erro.duracao_ms >= 0
        assert "texto" not in repr(erro)  # nunca o corpo da requisição


async def test_sucesso_nao_tem_erro(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    resposta = await cliente.post(URL, json={"texto": "ola"}, headers=CABECALHOS)
    await movimentacao.parar()

    assert repo.por_id(id_do_header(resposta)).erro is None


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

    gravada = repo.por_id(id_do_header(bloqueada))
    assert bloqueada.status_code == 429
    assert (gravada.situacao_requisicao, gravada.situacao_resposta) == ("ER", "ER")
    assert gravada.erro is not None and gravada.erro.codigo == "limite_excedido"


async def test_corpo_acima_do_limite_registra_so_o_tamanho(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    corpo = b'{"texto": "' + b"1" * 70_000 + b'"}'
    resposta = await cliente.post(URL, content=corpo, headers=CABECALHOS_JSON)
    await movimentacao.parar()

    gravada = repo.por_id(id_do_header(resposta))
    assert resposta.status_code == 413
    assert gravada.requisicao_conteudo is not None and str(len(corpo)) in gravada.requisicao_conteudo
    assert "1111" not in gravada.requisicao_conteudo
    assert gravada.erro is not None and gravada.erro.codigo == "corpo_muito_grande"


async def test_banco_lento_ou_fora_na_gravacao_nao_afeta_a_resposta(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    # Banco lento: a gravação fica presa num portão (sem cronômetro: não depende da velocidade da máquina).
    repo.portao = asyncio.Event()
    movimentacao.iniciar()
    resposta = await cliente.post(URL, json={"texto": "ola"}, headers=CABECALHOS)

    assert resposta.status_code == 200  # a resposta saiu com a gravação ainda presa
    identificador = id_do_header(resposta)  # o número nasce na API: sempre existe
    assert repo.registros == []
    repo.portao.set()
    async with asyncio.timeout(2):
        while not repo.registros:
            await asyncio.sleep(0.01)
    assert repo.por_id(identificador).status_http == 200

    # Banco fora: a gravação falha (o lote é perdido e o aviso vai para o log), e a resposta sai normalmente.
    repo.falha = True
    sem_banco = await cliente.post(URL, json={"texto": "ola"}, headers=CABECALHOS)
    await movimentacao.parar()
    assert sem_banco.status_code == 200 and ulid.valido(sem_banco.headers["x-requisicao"])
    assert movimentacao.gravador.perdidos >= 1


async def test_cada_requisicao_tem_um_ulid_proprio(cliente: AsyncClient, movimentacao: Movimentacao) -> None:
    ids = [
        id_do_header(await cliente.post(URL, json={"texto": "ola"}, headers=CABECALHOS)) for _ in range(30)
    ]
    await movimentacao.parar()

    assert len(set(ids)) == 30
    agora = datetime.now(UTC)
    assert all(0 <= (agora - ulid.momento(i)).total_seconds() < 30 for i in ids)


async def test_iniciar_grava_em_segundo_plano_e_parar_encerra(
    cliente: AsyncClient, repo: RepositorioFalso, movimentacao: Movimentacao
) -> None:
    movimentacao.iniciar()
    resposta = await cliente.post(URL, json={"texto": "   "}, headers=CABECALHOS)

    async with asyncio.timeout(2):
        while not repo.registros:  # sem parar(): a tarefa de fundo grava sozinha
            await asyncio.sleep(0.01)
    await movimentacao.parar()

    gravada = repo.por_id(id_do_header(resposta))
    assert (
        gravada.status_http == 422 and gravada.erro is not None and gravada.erro.codigo == "entrada_invalida"
    )
