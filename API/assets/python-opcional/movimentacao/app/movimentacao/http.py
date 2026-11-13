"""Movimentação na API: o ULID da requisição na chegada e uma só gravação, em segundo plano, no fim.

- Na chegada, a API gera o ULID (sem banco) e o guarda em ``request.state``.
- Depois da resposta, ela entra na fila (``gravacao.py``): o que chegou, o que foi respondido e, se foi
  erro (4xx/5xx), os metadados do erro. O ULID vai ao cliente no header ``X-Requisicao``.
- O banco nunca está no caminho da resposta: com ele fora ou lento, a fila enche e as gravações a mais
  se perdem (a API avisa no log).

Como ligar ao main.py: ver LEIAME.md.
"""

import time
from datetime import UTC, datetime

from fastapi import Request, Response

from app.movimentacao import ulid
from app.movimentacao.gravacao import GravadorEmLotes
from app.movimentacao.registros import RegistroErro, RegistroMovimentacao, RepositorioDeMovimentacao
from app.movimentacao.regras import conteudo_para_gravar, situacoes


class Movimentacao:
    def __init__(self, repositorio: RepositorioDeMovimentacao, ambiente: str) -> None:
        self._ambiente = ambiente[:20]
        self.gravador = GravadorEmLotes[RegistroMovimentacao](
            repositorio.gravar, "da movimentação", "a movimentação"
        )

    @staticmethod
    def da(request: Request) -> "Movimentacao | None":
        """A movimentação ligada à aplicação (app.state.movimentacao), se houver."""
        movimentacao: Movimentacao | None = getattr(request.app.state, "movimentacao", None)
        return movimentacao

    def iniciar(self) -> None:
        self.gravador.iniciar()

    async def parar(self) -> None:
        """Grava o que ainda estiver na fila."""
        await self.gravador.parar()

    def abrir(self, request: Request, conteudo: str | None) -> None:
        """Gera o ULID da requisição (sem banco) e guarda a chegada em request.state."""
        request.state.inicio = time.perf_counter()  # duração no log de erros
        request.state.chegada = datetime.now(UTC)
        request.state.movimentacao_id = ulid.novo(request.state.chegada)
        request.state.conteudo_requisicao = conteudo

    async def completar(self, request: Request, response: Response) -> Response:
        """Enfileira a requisição e a resposta (sem esperar o banco) e devolve o ULID em X-Requisicao."""
        identificador: str | None = getattr(request.state, "movimentacao_id", None)
        if identificador is None:
            return response
        iterador = getattr(response, "body_iterator", None)
        if iterador is None:
            corpo = bytes(response.body)
        else:  # resposta da rota (streaming): lê o corpo (pequeno) e devolve uma cópia com o mesmo conteúdo
            corpo = b"".join(
                [parte if isinstance(parte, bytes) else parte.encode() async for parte in iterador]
            )
            response = Response(
                content=corpo, status_code=response.status_code, headers=dict(response.headers)
            )
        situacao_requisicao, situacao_resposta = situacoes(response.status_code)
        self.gravador.registrar(
            RegistroMovimentacao(
                requisicao_id=identificador,
                metodo=request.method[:10],
                rota=request.url.path[:200],
                requisicao_conteudo=request.state.conteudo_requisicao,
                requisicao_em=request.state.chegada,
                status_http=response.status_code,
                resposta_conteudo=conteudo_para_gravar(corpo),
                resposta_em=datetime.now(UTC),
                situacao_requisicao=situacao_requisicao,
                situacao_resposta=situacao_resposta,
                erro=getattr(request.state, "erro_movimentacao", None),
            )
        )
        response.headers["X-Requisicao"] = identificador
        return response

    def registrar_erro(
        self, request: Request, status: int, codigo: str, titulo: str, tipo_excecao: str | None
    ) -> None:
        """Guarda os metadados do erro (gravados com a movimentação). Sem ULID, não grava."""
        if getattr(request.state, "movimentacao_id", None) is None:
            return
        inicio: float | None = getattr(request.state, "inicio", None)
        request.state.erro_movimentacao = RegistroErro(
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


def registrar_erro(
    request: Request, status: int, codigo: str, titulo: str, tipo_excecao: str | None = None
) -> None:
    """Atalho para o _resposta_erro do main.py: não faz nada se a movimentação não estiver ligada."""
    movimentacao = Movimentacao.da(request)
    if movimentacao is not None:
        movimentacao.registrar_erro(request, status, codigo, titulo, tipo_excecao)
