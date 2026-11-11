"""Oracle (python-oracledb, modo thin): DSN e tradução das falhas do driver para 503/504.

Use o banco plugável (ex.: localhost:1521/XEPDB1), nunca o CDB raiz (ver skill conexao-oracle).
"""

import logging
from collections.abc import Iterator
from contextlib import contextmanager

import oracledb

from app.bancos.erros import BancoExpirou, BancoIndisponivel

logger = logging.getLogger(__name__)

# DPY-4024: call_timeout estourado. ORA-01013: comando cancelado pelo tempo limite.
_EXPIROU = {"DPY-4024", "ORA-01013"}

# Login, instância, conexão perdida e listener. Os DPY-6xxx (rede) entram pelo prefixo.
_INDISPONIVEL = {
    "DPY-4005",  # conexão encerrada
    "DPY-4011",  # conexão caiu
    "ORA-01017",  # usuário/senha inválidos
    "ORA-01033",
    "ORA-01034",
    "ORA-01089",
    "ORA-03113",
    "ORA-03114",
    "ORA-03135",
    "ORA-12154",
    "ORA-12170",
    "ORA-12514",
    "ORA-12537",
    "ORA-12541",
    "ORA-12545",
    "ORA-28000",
}


def codigo_do_erro(exc: oracledb.Error) -> str:
    (erro,) = exc.args
    return str(getattr(erro, "full_code", type(exc).__name__))


def eh_indisponivel(codigo: str) -> bool:
    return codigo in _INDISPONIVEL or codigo.startswith(("DPY-6", "ORA-125", "ORA-126"))


@contextmanager
def traduzir_erros_oracle() -> Iterator[None]:
    """Envolve qualquer acesso ao banco (sync ou async):

    with traduzir_erros_oracle():
        await cursor.execute(...)
    """
    try:
        yield
    except oracledb.Error as exc:
        codigo = codigo_do_erro(exc)
        if codigo in _EXPIROU:
            raise BancoExpirou() from exc
        if eh_indisponivel(codigo):
            # Só o código: a mensagem do driver pode trazer detalhes da conexão.
            logger.error("oracle indisponível", extra={"codigo_oracle": codigo})
            raise BancoIndisponivel() from exc
        raise
