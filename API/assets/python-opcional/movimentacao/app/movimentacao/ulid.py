"""Identificador da requisição: ULID (26 caracteres, alfabeto Crockford), gerado na API, sem banco.

48 bits de hora em milissegundos + 80 bits aleatórios (``secrets``, gerador criptográfico). Dois ULID nunca
repetem na prática e o texto, em ordem alfabética, é a ordem cronológica. Não é sequencial e não reinicia a
cada ano.
"""

import secrets
from datetime import UTC, datetime, timedelta

TAMANHO = 26
_ALFABETO = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
_EPOCA = datetime(1970, 1, 1, tzinfo=UTC)
_UM_MS = timedelta(milliseconds=1)
_LIMITE_DE_HORA = 1 << 48


def novo(momento: datetime | None = None) -> str:
    """Um ULID novo; ``momento`` (com fuso) só para testes."""
    momento = momento or datetime.now(UTC)
    ms = (momento - _EPOCA) // _UM_MS
    if not 0 <= ms < _LIMITE_DE_HORA:
        raise ValueError("hora fora do intervalo de 48 bits de milissegundos")
    valor = (ms << 80) | int.from_bytes(secrets.token_bytes(10), "big")
    return "".join(_ALFABETO[(valor >> (5 * (TAMANHO - 1 - i))) & 31] for i in range(TAMANHO))


def valido(texto: object) -> bool:
    """26 caracteres do alfabeto, e hora dentro dos 48 bits (o primeiro caractere vai até 7)."""
    return (
        isinstance(texto, str)
        and len(texto) == TAMANHO
        and texto[0] <= "7"
        and all(c in _ALFABETO for c in texto)
    )


def momento(ulid: str) -> datetime:
    """A hora (UTC) que o ULID carrega, com precisão de milissegundo."""
    if not valido(ulid):
        raise ValueError("ULID inválido")
    ms = 0
    for c in ulid[:10]:
        ms = (ms << 5) | _ALFABETO.index(c)
    return _EPOCA + ms * _UM_MS
