"""Entrada JSON estruturada: aceita o contrato exato e rejeita todo o resto (mesmos casos do .NET)."""

import copy
import json
import os
from collections.abc import AsyncIterator
from typing import Any

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

from app.main import app
from tests.contrato import afirmar_problema

URL = "/api/v1/pedidos"
CABECALHOS = {"X-API-Key": os.environ["API_KEY"], "Content-Type": "application/json"}
VALIDO: dict[str, Any] = {
    "id_externo": "PED-2026-0001",
    "cliente": {"nome": "  Maria da Silva  ", "email": "maria@exemplo.com"},
    "itens": [
        {"descricao": "Caneta azul", "quantidade": 2, "valor_unitario_centavos": 150},
        {"descricao": "Caderno", "quantidade": 1, "valor_unitario_centavos": 2590},
    ],
    "observacao": None,
}


@pytest_asyncio.fixture
async def cliente() -> AsyncIterator[AsyncClient]:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as cliente_http:
        yield cliente_http


def _com(caminho: str, valor: Any) -> dict[str, Any]:
    """Cópia do corpo válido com um campo trocado (caminho com pontos; números = posição na lista)."""
    corpo = copy.deepcopy(VALIDO)
    *pais, ultimo = caminho.split(".")
    alvo: Any = corpo
    for parte in pais:
        alvo = alvo[int(parte)] if parte.isdigit() else alvo[parte]
    alvo[int(ultimo) if ultimo.isdigit() else ultimo] = valor
    return corpo


async def _enviar(cliente: AsyncClient, corpo: object) -> Any:
    return await cliente.post(URL, content=json.dumps(corpo).encode(), headers=CABECALHOS)


async def test_pedido_valido_e_totalizado_sem_devolver_dados_pessoais(cliente: AsyncClient) -> None:
    resposta = await _enviar(cliente, VALIDO)

    assert resposta.status_code == 200
    assert resposta.json() == {
        "id_externo": "PED-2026-0001",
        "quantidade_itens": 2,
        "quantidade_total": 3,
        "valor_total_centavos": 2890,
    }
    assert "Maria" not in resposta.text and "maria@" not in resposta.text


async def test_campos_opcionais_podem_faltar(cliente: AsyncClient) -> None:
    corpo = copy.deepcopy(VALIDO)
    del corpo["observacao"], corpo["cliente"]["email"]
    assert (await _enviar(cliente, corpo)).status_code == 200


@pytest.mark.parametrize(
    ("caminho", "valor"),
    [
        ("campo_extra", 1),  # campo desconhecido no topo
        ("cliente.campo_extra", 1),  # ... e aninhado
        ("itens.0.quantidade", "2"),  # texto no lugar de número: sem conversão
        ("itens.0.quantidade", 1.5),  # decimal no lugar de inteiro
        ("itens.0.quantidade", True),  # booleano no lugar de número
        ("itens.0.quantidade", 0),  # abaixo do mínimo
        ("itens.0.valor_unitario_centavos", -1),  # dinheiro negativo
        ("itens.0.descricao", "   "),  # vazio depois de remover os espaços
        ("cliente.nome", "Maria\x00"),  # caractere de controle
        ("cliente.nome", None),  # nulo em campo obrigatório
        ("cliente.email", "sem-arroba"),
        ("id_externo", "PED 01; DROP"),  # fora do padrão permitido
        ("itens", []),  # lista vazia
        ("itens", [VALIDO["itens"][0]] * 101),  # lista acima do máximo
        ("cliente", "Maria"),  # texto no lugar de objeto
    ],
    ids=lambda v: str(v)[:30],
)
async def test_forma_errada_responde_422(cliente: AsyncClient, caminho: str, valor: Any) -> None:
    resposta = await _enviar(cliente, _com(caminho, valor))
    afirmar_problema(resposta, 422, "entrada_invalida")


async def test_campo_obrigatorio_ausente_e_lista_no_topo_respondem_422(cliente: AsyncClient) -> None:
    sem_cliente = {k: v for k, v in VALIDO.items() if k != "cliente"}
    afirmar_problema(await _enviar(cliente, sem_cliente), 422, "entrada_invalida")
    afirmar_problema(await _enviar(cliente, [VALIDO]), 422, "entrada_invalida")


async def test_json_aninhado_fundo_demais_responde_400(cliente: AsyncClient) -> None:
    corpo = b'{"id_externo": "P1", "observacao": ' + b'{"a": ' * 9000 + b"1" + b"}" * 9001
    afirmar_problema(await cliente.post(URL, content=corpo, headers=CABECALHOS), 400, "requisicao_invalida")


async def test_erro_nao_ecoa_a_entrada(cliente: AsyncClient) -> None:
    resposta = await _enviar(cliente, _com("cliente.nome", "<script>alert(1)</script>\x07"))
    afirmar_problema(resposta, 422, "entrada_invalida")
    assert "script" not in resposta.text
