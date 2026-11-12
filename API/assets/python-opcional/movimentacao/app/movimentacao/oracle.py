"""Movimentação no Oracle (python-oracledb thin, async + pool). Usa as duas procedures e a tabela log_erros.

O usuário da aplicação não tem acesso às tabelas da movimentação: só executa as procedures (direitos do dono),
pelos sinônimos criados no script SQL (sem digitar o schema do dono).
"""

from dataclasses import asdict

import oracledb

from app.bancos.oracle import traduzir_erros_oracle
from app.movimentacao.registros import RegistroErro, RegistroResposta

_SQL_LOG_ERROS = (
    "insert into log_erros (ano, requisicao, request_id, metodo, rota, status, codigo, titulo, tipo_excecao,"
    " duracao_ms, ambiente) values (:ano, :requisicao, :request_id, :metodo, :rota, :status, :codigo,"
    " :titulo, :tipo_excecao, :duracao_ms, :ambiente)"
)


class MovimentacaoOracle:
    def __init__(self, pool: oracledb.AsyncConnectionPool, call_timeout_ms: int = 5000) -> None:
        self._pool = pool
        self._timeout = call_timeout_ms

    async def registrar_requisicao(self, metodo: str, rota: str, conteudo: str | None) -> tuple[int, int]:
        with traduzir_erros_oracle():
            async with self._pool.acquire() as conn:
                conn.call_timeout = self._timeout
                with conn.cursor() as cur:
                    ano = cur.var(int)
                    numero = cur.var(int)
                    await cur.callproc("registrar_requisicao", [metodo, rota, await _clob(conn, conteudo), ano, numero])
                await conn.commit()
        return int(ano.getvalue()), int(numero.getvalue())

    async def registrar_respostas(self, registros: list[RegistroResposta]) -> None:
        with traduzir_erros_oracle():
            async with self._pool.acquire() as conn:
                conn.call_timeout = self._timeout
                with conn.cursor() as cur:
                    for r in registros:
                        parametros: list[object] = [r.ano, r.requisicao, r.status_http, await _clob(conn, r.conteudo)]
                        parametros += [r.situacao_requisicao, r.situacao_resposta]
                        await cur.callproc("registrar_resposta", parametros)
                await conn.commit()

    async def gravar_erros(self, registros: list[RegistroErro]) -> None:
        with traduzir_erros_oracle():
            async with self._pool.acquire() as conn:
                conn.call_timeout = self._timeout
                with conn.cursor() as cur:
                    await cur.executemany(_SQL_LOG_ERROS, [asdict(r) for r in registros])
                await conn.commit()


async def _clob(conn: oracledb.AsyncConnection, texto: str | None) -> oracledb.AsyncLOB | None:
    """CLOB temporário: sem isto, texto acima de 32767 bytes não passa como parâmetro de procedure."""
    if texto is None:
        return None
    lob: oracledb.AsyncLOB = await conn.createlob(oracledb.DB_TYPE_CLOB, texto)
    return lob
