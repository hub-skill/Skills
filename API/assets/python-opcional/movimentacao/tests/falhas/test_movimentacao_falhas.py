"""Movimentação com o banco fora do ar: drivers reais (psycopg e python-oracledb) contra uma porta sem
servidor. A falha vira BancoIndisponivel (a gravação em segundo plano só a registra no log), nunca o erro cru
do driver. Não precisa de banco no ar: roda também no CI.
"""

from datetime import UTC, datetime

import oracledb
import pytest
from psycopg_pool import AsyncConnectionPool

from app.bancos.erros import BancoIndisponivel
from app.bancos.postgres import montar_conninfo
from app.movimentacao.oracle import MovimentacaoOracle
from app.movimentacao.postgres import MovimentacaoPostgres
from app.movimentacao.registros import RegistroErro, RegistroMovimentacao

PORTA_SEM_SERVIDOR = 1
_AGORA = datetime.now(UTC)
ERRO = RegistroErro("r1", "POST", "/x", 503, "banco_indisponivel", "Banco fora.", None, 1, "teste")
REGISTRO = RegistroMovimentacao(
    "01JC4M7Z9Q8T2X5V6B3N0WYKHD", "POST", "/x", "{}", _AGORA, 503, "{}", _AGORA, "OK", "ER", ERRO
)


async def test_postgres_fora_do_ar_vira_banco_indisponivel() -> None:
    pool = AsyncConnectionPool(
        conninfo=montar_conninfo("127.0.0.1", PORTA_SEM_SERVIDOR, "banco", "usuario"),
        kwargs={"password": "x"},  # senha falsa: nada aqui conecta de verdade
        open=False,
        timeout=2,
    )
    await pool.open(wait=False)
    try:
        with pytest.raises(BancoIndisponivel):
            await MovimentacaoPostgres(pool, "app").gravar([REGISTRO])
    finally:
        await pool.close()


async def test_oracle_fora_do_ar_vira_banco_indisponivel() -> None:
    pool = oracledb.create_pool_async(
        user="usuario",
        password="x",
        dsn=f"127.0.0.1:{PORTA_SEM_SERVIDOR}/XEPDB1",
        min=1,
        max=1,  # noqa: S106
    )
    try:
        with pytest.raises(BancoIndisponivel):
            await MovimentacaoOracle(pool).gravar([REGISTRO])
    finally:
        await pool.close(force=True)


def test_nome_de_esquema_invalido_e_recusado() -> None:
    # O nome do schema entra no SQL: só identificador simples.
    with pytest.raises(ValueError, match="esquema"):
        MovimentacaoPostgres(None, "app; drop table x")  # type: ignore[arg-type]
