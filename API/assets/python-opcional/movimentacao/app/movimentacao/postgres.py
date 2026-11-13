"""Movimentação no PostgreSQL (psycopg 3 async + pool): as tabelas movimentacao e log_erros do script SQL.

O usuário da aplicação só tem INSERT nas duas tabelas (nada de leitura, alteração ou exclusão). Cada lote
é uma transação: a movimentação e, quando houver, o seu erro. A sessão da API é somente leitura
(default_transaction_read_only): só as transações daqui pedem escrita.
"""

from psycopg_pool import AsyncConnectionPool

from app.bancos.postgres import traduzir_erros_postgres
from app.movimentacao.registros import RegistroMovimentacao

_SQL_MOVIMENTACAO = (
    "insert into {esquema}.movimentacao (requisicao_id, metodo, rota, requisicao_conteudo, requisicao_em,"
    " situacao_requisicao, status_http, resposta_conteudo, resposta_em, situacao_resposta)"
    " values (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s)"
)
_SQL_ERRO = (
    "insert into {esquema}.log_erros (requisicao_id, request_id, metodo, rota, status, codigo, titulo,"
    " tipo_excecao, duracao_ms, ambiente) values (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s)"
)


class MovimentacaoPostgres:
    def __init__(self, pool: AsyncConnectionPool, esquema: str) -> None:
        if not esquema.isidentifier():  # o nome do schema vem da configuração e entra no SQL
            raise ValueError("esquema inválido")
        self._pool = pool
        self._esquema = esquema

    async def gravar(self, registros: list[RegistroMovimentacao]) -> None:
        movimentacoes = [
            (
                r.requisicao_id,
                r.metodo,
                r.rota,
                r.requisicao_conteudo,
                r.requisicao_em,
                r.situacao_requisicao,
                r.status_http,
                r.resposta_conteudo,
                r.resposta_em,
                r.situacao_resposta,
            )
            for r in registros
        ]
        erros = [
            (
                r.requisicao_id,
                r.erro.request_id,
                r.erro.metodo,
                r.erro.rota,
                r.erro.status,
                r.erro.codigo,
                r.erro.titulo,
                r.erro.tipo_excecao,
                r.erro.duracao_ms,
                r.erro.ambiente,
            )
            for r in registros
            if r.erro is not None
        ]
        with traduzir_erros_postgres():
            async with self._pool.connection() as conn, conn.transaction():
                await conn.execute("set transaction read write")  # a sessão é somente leitura
                async with conn.cursor() as cur:
                    await cur.executemany(_SQL_MOVIMENTACAO.format(esquema=self._esquema), movimentacoes)
                    if erros:  # depois da movimentação: o erro aponta para ela (chave estrangeira)
                        await cur.executemany(_SQL_ERRO.format(esquema=self._esquema), erros)
