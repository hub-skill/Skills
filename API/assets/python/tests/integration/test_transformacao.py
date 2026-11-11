import os
from collections.abc import AsyncIterator

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

from app.main import app

URL = "/api/v1/transformar"
CABECALHOS = {"X-API-Key": os.environ["API_KEY"]}


@pytest_asyncio.fixture
async def cliente() -> AsyncIterator[AsyncClient]:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as cliente_http:
        yield cliente_http


async def test_transforma_texto_e_remove_espacos(cliente: AsyncClient) -> None:
    resposta = await cliente.post(URL, json={"texto": "  ola  "}, headers=CABECALHOS)
    assert resposta.status_code == 200
    assert resposta.json() == {"resultado": "OLA"}


async def test_aceita_unicode_e_tamanho_maximo(cliente: AsyncClient) -> None:
    resposta = await cliente.post(URL, json={"texto": "ç" * 10_000}, headers=CABECALHOS)
    assert resposta.status_code == 200
    assert resposta.json()["resultado"] == "Ç" * 10_000


async def test_rejeita_sem_chave(cliente: AsyncClient) -> None:
    resposta = await cliente.post(URL, json={"texto": "ola"})
    assert resposta.status_code == 401


async def test_rejeita_chave_errada(cliente: AsyncClient) -> None:
    resposta = await cliente.post(URL, json={"texto": "ola"}, headers={"X-API-Key": "errada"})
    assert resposta.status_code == 401


@pytest.mark.parametrize(
    "texto",
    ["", "   ", "a" * 10_001, "ola\x00mundo", "ola\x07"],
    ids=["vazio", "so_espacos", "acima_do_maximo", "byte_nulo", "controle"],
)
async def test_rejeita_entrada_invalida(cliente: AsyncClient, texto: str) -> None:
    resposta = await cliente.post(URL, json={"texto": texto}, headers=CABECALHOS)
    assert resposta.status_code == 422
    assert resposta.json()["error"]["code"] == "entrada_invalida"
    assert resposta.json()["error"]["request_id"] is not None


async def test_nao_ecoa_entrada_maliciosa_no_erro(cliente: AsyncClient) -> None:
    payload = "'; DROP TABLE usuarios; --\x00"
    resposta = await cliente.post(URL, json={"texto": payload}, headers=CABECALHOS)
    assert resposta.status_code == 422
    assert payload not in resposta.text
