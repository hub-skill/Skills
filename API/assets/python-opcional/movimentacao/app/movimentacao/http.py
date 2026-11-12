"""Movimentação na API: numera a chegada, completa a resposta em segundo plano e registra os erros.

- Na chegada, a API espera o banco devolver o número (ano, requisicao) por no máximo ``prazo_para_numerar``
  segundos: sem número no prazo, a requisição segue normalmente, sem movimentação (e sem log de erros).
- A resposta completa a linha em segundo plano (fila e lotes de gravacao.py) e o número vai ao cliente no
  header ``X-Requisicao`` (ex.: 2026-000123).
- O erro (4xx/5xx) vai para a tabela log_erros com a chave da movimentação; sem chave, não é gravado.

Como ligar ao main.py: ver LEIAME.md.
"""

import asyncio
import logging
import time

from fastapi import Request, Response

from app.movimentacao.gravacao import GravadorEmLotes
from app.movimentacao.regras import conteudo_para_gravar, numero_formatado, situacoes
from app.movimentacao.registros import RegistroErro, RegistroResposta, RepositorioDeMovimentacao

logger = logging.getLogger(__name__)

PRAZO_PARA_NUMERAR_SEGUNDOS = 1.0


class Movimentacao:
    def __init__(
        self,
        repositorio: RepositorioDeMovimentacao,
        ambiente: str,
        prazo_para_numerar: float = PRAZO_PARA_NUMERAR_SEGUNDOS,
    ) -> None:
        self._repositorio = repositorio
        self._ambiente = ambiente[:20]
        self._prazo = prazo_para_numerar
        self.erros = GravadorEmLotes[RegistroErro](repositorio.gravar_erros, "do log de erros", "o log de erros")
        self.respostas = GravadorEmLotes[RegistroResposta](
            repositorio.registrar_respostas, "da movimentação", "a resposta na movimentação"
        )

    @staticmethod
    def da(request: Request) -> "Movimentacao | None":
        """A movimentação ligada à aplicação (app.state.movimentacao), se houver."""
        movimentacao: Movimentacao | None = getattr(request.app.state, "movimentacao", None)
        return movimentacao

    def iniciar(self) -> None:
        self.erros.iniciar()
        self.respostas.iniciar()

    async def parar(self) -> None:
        """Grava o que ainda estiver nas filas (as respostas primeiro: os erros dependem da movimentação)."""
        await self.respostas.parar()
        await self.erros.parar()

    async def numerar(self, request: Request, conteudo: str | None) -> None:
        """Grava a chegada e guarda (ano, requisicao) em request.state.movimentacao."""
        request.state.inicio = time.perf_counter()
        try:
            async with asyncio.timeout(self._prazo):
                request.state.movimentacao = await self._repositorio.registrar_requisicao(
                    request.method[:10], request.url.path[:200], conteudo
                )
        except Exception as exc:  # noqa: BLE001 - sem número a API continua respondendo
            logger.warning("requisição sem número na movimentação", extra={"erro": type(exc).__name__})

    async def completar(self, request: Request, response: Response) -> Response:
        """Põe a resposta na fila (sem esperar o banco) e devolve o número no header X-Requisicao."""
        numero: tuple[int, int] | None = getattr(request.state, "movimentacao", None)
        if numero is None:
            return response
        iterador = getattr(response, "body_iterator", None)
        if iterador is None:
            corpo = bytes(response.body)
        else:  # resposta da rota (streaming): lê o corpo (pequeno) e devolve uma cópia com o mesmo conteúdo
            corpo = b"".join([parte if isinstance(parte, bytes) else parte.encode() async for parte in iterador])
            response = Response(content=corpo, status_code=response.status_code, headers=dict(response.headers))
        situacao_requisicao, situacao_resposta = situacoes(response.status_code)
        self.respostas.registrar(
            RegistroResposta(
                ano=numero[0],
                requisicao=numero[1],
                status_http=response.status_code,
                conteudo=conteudo_para_gravar(corpo),
                situacao_requisicao=situacao_requisicao,
                situacao_resposta=situacao_resposta,
            )
        )
        response.headers["X-Requisicao"] = numero_formatado(*numero)
        return response

    def registrar_erro(self, request: Request, status: int, codigo: str, titulo: str, tipo_excecao: str | None) -> None:
        """Põe o erro na fila da log_erros: só metadados. Sem número de requisição, não grava."""
        numero: tuple[int, int] | None = getattr(request.state, "movimentacao", None)
        if numero is None:
            return
        inicio: float | None = getattr(request.state, "inicio", None)
        self.erros.registrar(
            RegistroErro(
                ano=numero[0],
                requisicao=numero[1],
                request_id=str(getattr(request.state, "request_id", "") or "-"),
                metodo=request.method[:10],
                rota=request.url.path[:200],
                status=status,
                codigo=codigo[:40],
                titulo=titulo[:200],
                tipo_excecao=tipo_excecao[:200] if tipo_excecao else None,
                duracao_ms=round((time.perf_counter() - inicio) * 1000) if inicio is not None else None,
                ambiente=self._ambiente,
            )
        )


def registrar_erro(request: Request, status: int, codigo: str, titulo: str, tipo_excecao: str | None = None) -> None:
    """Atalho para o _resposta_erro do main.py: não faz nada se a movimentação não estiver ligada."""
    movimentacao = Movimentacao.da(request)
    if movimentacao is not None:
        movimentacao.registrar_erro(request, status, codigo, titulo, tipo_excecao)
