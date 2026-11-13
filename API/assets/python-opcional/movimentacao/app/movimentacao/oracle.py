"""Movimentação no Oracle (python-oracledb thin, async + pool). Usa as tabelas movimentacao e log_erros.

O usuário da aplicação só tem INSERT nas duas tabelas, pelos sinônimos criados no script SQL (sem digitar
o schema do dono). Cada lote é uma transação: a movimentação e, quando houver, o seu erro.
"""

from datetime import UTC, datetime

import oracledb

from app.bancos.oracle import traduzir_erros_oracle
from app.movimentacao.registros import RegistroMovimentacao

# Os instantes vão como UTC sem fuso e viram TIMESTAMP WITH TIME ZONE (sem depender do fuso de quem lê).
_SQL_MOVIMENTACAO = (
    "insert into movimentacao (requisicao_id, metodo, rota, requisicao_conteudo, requisicao_em,"
    " situacao_requisicao, status_http, resposta_conteudo, resposta_em, situacao_resposta)"
    " values (:requisicao_id, :metodo, :rota, :requisicao_conteudo,"
    " from_tz(cast(:requisicao_em as timestamp), 'UTC'), :situacao_requisicao, :status_http,"
    " :resposta_conteudo, from_tz(cast(:resposta_em as timestamp), 'UTC'), :situacao_resposta)"
)
_SQL_ERRO = (
    "insert into log_erros (requisicao_id, request_id, metodo, rota, status, codigo, titulo, tipo_excecao,"
    " duracao_ms, ambiente) values (:requisicao_id, :request_id, :metodo, :rota, :status, :codigo, :titulo,"
    " :tipo_excecao, :duracao_ms, :ambiente)"
)


class MovimentacaoOracle:
    def __init__(self, pool: oracledb.AsyncConnectionPool, call_timeout_ms: int = 5000) -> None:
        self._pool = pool
        self._timeout = call_timeout_ms

    async def gravar(self, registros: list[RegistroMovimentacao]) -> None:
        with traduzir_erros_oracle():
            async with self._pool.acquire() as conn:
                conn.call_timeout = self._timeout
                with conn.cursor() as cur:
                    for (
                        r
                    ) in registros:  # CLOB não vai em executemany: um insert por registro, na mesma transação
                        # Sem isto o driver envia datetime como DATE e perde as frações de segundo.
                        cur.setinputsizes(
                            requisicao_em=oracledb.DB_TYPE_TIMESTAMP, resposta_em=oracledb.DB_TYPE_TIMESTAMP
                        )
                        await cur.execute(
                            _SQL_MOVIMENTACAO,
                            {
                                "requisicao_id": r.requisicao_id,
                                "metodo": r.metodo,
                                "rota": r.rota,
                                "requisicao_conteudo": await _clob(conn, r.requisicao_conteudo),
                                "requisicao_em": _utc_sem_fuso(r.requisicao_em),
                                "situacao_requisicao": r.situacao_requisicao,
                                "status_http": r.status_http,
                                "resposta_conteudo": await _clob(conn, r.resposta_conteudo),
                                "resposta_em": _utc_sem_fuso(r.resposta_em),
                                "situacao_resposta": r.situacao_resposta,
                            },
                        )
                        if r.erro is not None:
                            await cur.execute(
                                _SQL_ERRO,
                                {
                                    "requisicao_id": r.requisicao_id,
                                    "request_id": r.erro.request_id,
                                    "metodo": r.erro.metodo,
                                    "rota": r.erro.rota,
                                    "status": r.erro.status,
                                    "codigo": r.erro.codigo,
                                    "titulo": r.erro.titulo,
                                    "tipo_excecao": r.erro.tipo_excecao,
                                    "duracao_ms": r.erro.duracao_ms,
                                    "ambiente": r.erro.ambiente,
                                },
                            )
                await conn.commit()


def _utc_sem_fuso(momento: datetime) -> datetime:
    return momento.astimezone(UTC).replace(tzinfo=None)


async def _clob(conn: oracledb.AsyncConnection, texto: str | None) -> oracledb.AsyncLOB | None:
    """CLOB temporário: sem isto, texto acima de 32767 bytes não passa como parâmetro."""
    if texto is None:
        return None
    lob: oracledb.AsyncLOB = await conn.createlob(oracledb.DB_TYPE_CLOB, texto)
    return lob
