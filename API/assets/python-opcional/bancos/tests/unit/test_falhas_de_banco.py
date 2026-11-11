"""Drivers reais (psycopg e python-oracledb) apontando para uma porta sem servidor: a falha tem de virar
BancoIndisponivel (503 na API), e não escapar como exceção do driver (500).
Não precisa de banco no ar: roda também no CI.
Usa conexões síncronas (sem a questão do event loop no Windows).
"""

from types import SimpleNamespace

import oracledb
import psycopg
import pytest
from psycopg import errors as pgerr

from app.bancos.erros import BancoExpirou, BancoIndisponivel
from app.bancos.oracle import eh_indisponivel, traduzir_erros_oracle
from app.bancos.postgres import erro_de_conexao, montar_conninfo, options_sessao, traduzir_erros_postgres

PORTA_SEM_SERVIDOR = 1


def test_postgres_fora_do_ar_vira_banco_indisponivel() -> None:
    conninfo = montar_conninfo("127.0.0.1", PORTA_SEM_SERVIDOR, "banco", "usuario")
    with pytest.raises(BancoIndisponivel), traduzir_erros_postgres():
        psycopg.connect(conninfo, password="x", connect_timeout=2)  # noqa: S106 - senha falsa


def test_oracle_fora_do_ar_vira_banco_indisponivel() -> None:
    with pytest.raises(BancoIndisponivel), traduzir_erros_oracle():
        oracledb.connect(user="usuario", password="x", dsn=f"127.0.0.1:{PORTA_SEM_SERVIDOR}/XEPDB1")  # noqa: S106


def test_postgres_statement_timeout_vira_banco_expirou() -> None:
    with pytest.raises(BancoExpirou), traduzir_erros_postgres():
        raise pgerr.QueryCanceled("canceling statement due to statement timeout")


def test_erro_de_programacao_continua_sendo_erro() -> None:
    # Tabela inexistente não é "banco fora": deve virar 500 (bug), não 503.
    with pytest.raises(pgerr.UndefinedTable), traduzir_erros_postgres():
        raise pgerr.UndefinedTable("relation does not exist")


def test_postgres_erro_operacional_de_negocio_continua_sendo_erro() -> None:
    # Deadlock é OperationalError, mas com o banco no ar: quem chamou decide (repetir a transação).
    with pytest.raises(pgerr.DeadlockDetected), traduzir_erros_postgres():
        raise pgerr.DeadlockDetected("deadlock detected")


def test_postgres_conexao_fechada_vira_banco_indisponivel() -> None:
    with pytest.raises(BancoIndisponivel), traduzir_erros_postgres():
        raise psycopg.InterfaceError("the connection is closed")


def _erro_oracle(codigo: str) -> oracledb.DatabaseError:
    # O python-oracledb guarda em args[0] um objeto com full_code (ex.: "DPY-4024").
    return oracledb.DatabaseError(SimpleNamespace(full_code=codigo))


def test_oracle_call_timeout_vira_banco_expirou() -> None:
    with pytest.raises(BancoExpirou), traduzir_erros_oracle():
        raise _erro_oracle("DPY-4024")


def test_oracle_erro_de_programacao_continua_sendo_erro() -> None:
    with pytest.raises(oracledb.DatabaseError), traduzir_erros_oracle():
        raise _erro_oracle("ORA-00942")


@pytest.mark.parametrize(
    ("sqlstate", "indisponivel"),
    [
        ("08006", True),  # conexão perdida
        ("28P01", True),  # senha errada
        ("53300", True),  # conexões esgotadas
        ("57P01", True),  # servidor desligando
        ("42P01", False),  # tabela inexistente: erro de programação
        ("23505", False),  # unicidade: trate no repositório
        (None, False),
    ],
)
def test_postgres_classifica_sqlstate(sqlstate: str | None, indisponivel: bool) -> None:
    assert erro_de_conexao(sqlstate) is indisponivel


@pytest.mark.parametrize(
    ("codigo", "indisponivel"),
    [
        ("DPY-6005", True),  # não conectou (rede)
        ("DPY-6001", True),  # serviço não registrado no listener
        ("ORA-12541", True),  # sem listener
        ("ORA-01017", True),  # senha errada
        ("ORA-12600", True),  # faixa 125xx-126xx de rede
        ("ORA-00942", False),  # tabela inexistente: erro de programação
    ],
)
def test_oracle_classifica_codigos(codigo: str, indisponivel: bool) -> None:
    assert eh_indisponivel(codigo) is indisponivel


def test_conninfo_sem_senha_e_options_de_sessao() -> None:
    conninfo = montar_conninfo("h", 5432, "b", "u", sslmode="require")
    assert "password" not in conninfo
    assert "sslmode=require" in conninfo and "connect_timeout=5" in conninfo
    assert options_sessao(somente_leitura=True, statement_timeout_ms=3000) == (
        "-c statement_timeout=3000 -c default_transaction_read_only=on"
    )
    assert "read_only" not in options_sessao(somente_leitura=False)
