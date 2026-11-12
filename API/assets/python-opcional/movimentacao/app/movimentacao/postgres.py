"""Movimentação no PostgreSQL (psycopg 3 async + pool): as duas funções e a log_erros do script SQL.

O usuário da aplicação não tem acesso às tabelas da movimentação: só executa as funções (SECURITY DEFINER).
A sessão da API é somente leitura (default_transaction_read_only): só as transações daqui pedem escrita.
"""

from psycopg_pool import AsyncConnectionPool

from app.bancos.postgres import traduzir_erros_postgres
from app.movimentacao.registros import RegistroErro, RegistroResposta

_SQL_REQUISICAO = (
    "select ano, requisicao from {esquema}.registrar_requisicao(%s::varchar, %s::varchar, %s::text)"
)
_SQL_RESPOSTA = (
    "select {esquema}.registrar_resposta(%s::smallint, %s::bigint, %s::smallint, %s::text,"
    " %s::char(2), %s::char(2))"
)
_SQL_LOG_ERROS = (
    "insert into {esquema}.log_erros (ano, requisicao, request_id, metodo, rota, status, codigo, titulo,"
    " tipo_excecao, duracao_ms, ambiente) values (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)"
)


class MovimentacaoPostgres:
    def __init__(self, pool: AsyncConnectionPool, esquema: str) -> None:
        if not esquema.isidentifier():  # o nome do schema vem da configuração e entra no SQL
            raise ValueError("esquema inválido")
        self._pool = pool
        self._esquema = esquema

    async def registrar_requisicao(self, metodo: str, rota: str, conteudo: str | None) -> tuple[int, int]:
        sql = _SQL_REQUISICAO.format(esquema=self._esquema)
        with traduzir_erros_postgres():
            async with self._pool.connection() as conn, conn.transaction():
                await conn.execute("set transaction read write")  # a sessão é somente leitura
                cur = await conn.execute(sql, (metodo, rota, conteudo))
                linha = await cur.fetchone()
        if linha is None:  # pragma: no cover - a função sempre devolve uma linha
            raise RuntimeError("registrar_requisicao não devolveu o número")
        return int(linha[0]), int(linha[1])

    async def registrar_respostas(self, registros: list[RegistroResposta]) -> None:
        sql = _SQL_RESPOSTA.format(esquema=self._esquema)
        linhas = [
            (r.ano, r.requisicao, r.status_http, r.conteudo, r.situacao_requisicao, r.situacao_resposta)
            for r in registros
        ]
        with traduzir_erros_postgres():
            async with self._pool.connection() as conn, conn.transaction():
                await conn.execute("set transaction read write")
                async with conn.cursor() as cur:
                    await cur.executemany(sql, linhas)

    async def gravar_erros(self, registros: list[RegistroErro]) -> None:
        sql = _SQL_LOG_ERROS.format(esquema=self._esquema)
        linhas = [
            (
                r.ano, r.requisicao, r.request_id, r.metodo, r.rota, r.status, r.codigo, r.titulo,
                r.tipo_excecao, r.duracao_ms, r.ambiente,
            )
            for r in registros
        ]
        with traduzir_erros_postgres():
            async with self._pool.connection() as conn, conn.transaction():
                await conn.execute("set transaction read write")
                async with conn.cursor() as cur:
                    await cur.executemany(sql, linhas)
