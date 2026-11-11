"""PostgreSQL (psycopg 3): conninfo seguro e tradução das falhas do driver para 503/504.

Windows + async: o psycopg async não funciona com o ProactorEventLoop. Rode o uvicorn com
--loop asyncio:SelectorEventLoop (ver skill conexao-postgresql).
"""

import logging
from collections.abc import Iterator
from contextlib import contextmanager

import psycopg
from psycopg import errors as pgerr

from app.bancos.erros import BancoExpirou, BancoIndisponivel

logger = logging.getLogger(__name__)

# Classes de SQLSTATE de conexão (08), autenticação (28), recursos (53) e servidor desligando (57P).
_PREFIXOS_INDISPONIVEL = ("08", "28", "53", "57P")


def montar_conninfo(
    host: str,
    porta: int,
    banco: str,
    usuario: str,
    *,
    sslmode: str = "prefer",
    aplicacao: str = "projeto-api",
) -> str:
    """conninfo SEM a senha: passe password=... à parte (kwargs do pool), para ela não aparecer em logs.

    Use options_sessao() nos kwargs para sessão somente leitura e statement_timeout.
    Servidor remoto ou nuvem: sslmode="require" ou "verify-full".
    """
    return (
        f"host={host} port={porta} dbname={banco} user={usuario} sslmode={sslmode}"
        f" connect_timeout=5 application_name={aplicacao}"
    )


def options_sessao(*, somente_leitura: bool, statement_timeout_ms: int = 5000) -> str:
    """Valor de 'options' da conexão: statement_timeout (estourou = 504) e, se pedido, sessão read only."""
    opcoes = f"-c statement_timeout={statement_timeout_ms}"
    return opcoes + (" -c default_transaction_read_only=on" if somente_leitura else "")


def erro_de_conexao(sqlstate: str | None) -> bool:
    return sqlstate is not None and sqlstate.startswith(_PREFIXOS_INDISPONIVEL)


@contextmanager
def traduzir_erros_postgres() -> Iterator[None]:
    """Envolve qualquer acesso ao banco (sync ou async):

    with traduzir_erros_postgres():
        await conn.execute(...)
    """
    try:
        yield
    except pgerr.QueryCanceled as exc:  # 57014: statement_timeout
        raise BancoExpirou() from exc
    except psycopg.OperationalError as exc:
        # Conexão recusada, servidor fora, senha errada, conexões esgotadas. No Windows, porta fechada
        # chega como ConnectionTimeout (subclasse de OperationalError): também é "indisponível".
        # Na fase de conexão o sqlstate vem None; depois de conectado, vale a classe do SQLSTATE.
        if exc.sqlstate is None or erro_de_conexao(exc.sqlstate):
            logger.error("postgres indisponível", extra={"erro": type(exc).__name__})
            raise BancoIndisponivel() from exc
        raise
    except psycopg.InterfaceError as exc:
        # Conexão fechada ou pool encerrado.
        logger.error("postgres indisponível", extra={"erro": type(exc).__name__})
        raise BancoIndisponivel() from exc
