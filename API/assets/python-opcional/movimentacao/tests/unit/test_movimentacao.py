"""Regras da movimentação sem banco: máscara de CPF, limite do conteúdo, situações, rotas e a fila."""

import asyncio
import logging

import pytest

from app.movimentacao.gravacao import GravadorEmLotes
from app.movimentacao.regras import (
    LIMITE_DO_CONTEUDO,
    conteudo_da_requisicao,
    conteudo_para_gravar,
    mascarar_cpfs,
    movimentar,
    numero_formatado,
    situacoes,
)


@pytest.mark.parametrize(
    ("entrada", "esperado"),
    [
        ("529.982.247-25", "***.982.247-**"),
        ("52998224725", "***.982.247-**"),
        ("529982.247-25", "***.982.247-**"),
        ('{"documento": "529.982.247-25", "nome": "Ana"}', '{"documento": "***.982.247-**", "nome": "Ana"}'),
        ("a 52998224725 e 11144477735 juntos", "a ***.982.247-** e ***.444.777-** juntos"),
        ("19.182.685/0001-62", "19.182.685/0001-62"),  # CNPJ formatado não é CPF
        ("19182685000162", "19182685000162"),  # CNPJ só dígitos (14) não é CPF
        ("123456789012", "123456789012"),  # 12 dígitos: não casa
        ("sem numero nenhum", "sem numero nenhum"),
    ],
)
def test_mascara_todo_cpf_em_qualquer_formato(entrada: str, esperado: str) -> None:
    assert mascarar_cpfs(entrada) == esperado


def test_conteudo_vazio_vira_nulo_e_qualquer_formato_vira_texto() -> None:
    assert conteudo_para_gravar(b"") is None
    assert conteudo_para_gravar(b"<a>1</a>") == "<a>1</a>"
    assert conteudo_para_gravar(b"ol\xff") == "ol�"  # byte inválido não derruba a gravação
    assert conteudo_para_gravar("José Conceição".encode()) == "José Conceição"


def test_conteudo_acima_do_limite_e_cortado_com_aviso() -> None:
    texto = conteudo_para_gravar(b"a" * (LIMITE_DO_CONTEUDO + 500))
    assert texto is not None and len(texto) == LIMITE_DO_CONTEUDO
    assert texto.endswith("[cortado em 32768 caracteres]")
    assert conteudo_para_gravar(b"a" * LIMITE_DO_CONTEUDO) == "a" * LIMITE_DO_CONTEUDO  # no limite: inteiro


def test_corpo_acima_do_limite_da_api_grava_so_o_tamanho() -> None:
    assert conteudo_da_requisicao(b"", "70000", acima_do_limite=True) == "(corpo de 70000 bytes, acima do limite: não gravado)"
    assert conteudo_da_requisicao(b"{}", None, acima_do_limite=False) == "{}"


@pytest.mark.parametrize(
    ("status", "esperado"),
    [
        (200, ("OK", "OK")),
        (301, ("OK", "OK")),
        (400, ("ER", "ER")),
        (401, ("ER", "ER")),
        (422, ("ER", "ER")),
        (429, ("ER", "ER")),
        (500, ("OK", "ER")),
        (503, ("OK", "ER")),
        (504, ("OK", "ER")),
    ],
)
def test_situacoes_da_requisicao_e_da_resposta(status: int, esperado: tuple[str, str]) -> None:
    assert situacoes(status) == esperado


def test_numero_formatado_e_rotas_sem_movimentacao() -> None:
    assert numero_formatado(2026, 123) == "2026-000123"
    assert numero_formatado(2027, 1_234_567) == "2027-1234567"
    assert not movimentar("/health/live") and not movimentar("/health/ready") and not movimentar("/metrics")
    assert movimentar("/api/v1/transformar") and movimentar("/nao-existe") and movimentar("/")


class DestinoFalso:
    def __init__(self, falhar_vezes: int = 0) -> None:
        self.lotes: list[list[int]] = []
        self.falhar_vezes = falhar_vezes

    async def gravar(self, registros: list[int]) -> None:
        if self.falhar_vezes:
            self.falhar_vezes -= 1
            raise ConnectionError("banco fora")
        self.lotes.append(list(registros))


async def _esperar(condicao: object, prazo: float = 2.0) -> None:
    async with asyncio.timeout(prazo):
        while not condicao():  # type: ignore[operator]
            await asyncio.sleep(0.01)


def _gravador(destino: DestinoFalso, capacidade: int = 1000, lote: int = 100) -> GravadorEmLotes[int]:
    return GravadorEmLotes(destino.gravar, "de teste", "o teste", capacidade, lote)


async def test_grava_em_lotes_do_tamanho_configurado() -> None:
    destino = DestinoFalso()
    gravador = _gravador(destino, lote=2)
    for n in range(5):
        gravador.registrar(n)

    gravador.iniciar()
    await _esperar(lambda: sum(map(len, destino.lotes)) == 5)
    await gravador.parar()

    assert destino.lotes == [[0, 1], [2, 3], [4]]


async def test_fila_cheia_descarta_sem_bloquear(caplog: pytest.LogCaptureFixture) -> None:
    gravador = _gravador(DestinoFalso(), capacidade=2)
    with caplog.at_level(logging.WARNING):
        for n in range(3):
            gravador.registrar(n)

    assert gravador.descartados == 1
    assert "fila de teste cheia" in caplog.text


async def test_falha_do_banco_perde_o_lote_e_continua(caplog: pytest.LogCaptureFixture) -> None:
    destino = DestinoFalso(falhar_vezes=1)
    gravador = _gravador(destino)
    gravador.iniciar()

    with caplog.at_level(logging.WARNING):
        gravador.registrar(1)
        await _esperar(lambda: gravador.perdidos == 1)
        gravador.registrar(2)
        await _esperar(lambda: destino.lotes == [[2]])
    await gravador.parar()

    assert "falha ao gravar o teste no banco" in caplog.text
    assert "banco fora" not in caplog.text  # só o tipo da exceção vai para o log


async def test_parar_grava_o_que_ficou_na_fila() -> None:
    destino = DestinoFalso()
    gravador = _gravador(destino, lote=2)
    for n in range(3):
        gravador.registrar(n)

    await gravador.parar()  # sem iniciar: tudo ainda na fila

    assert destino.lotes == [[0, 1], [2]]
