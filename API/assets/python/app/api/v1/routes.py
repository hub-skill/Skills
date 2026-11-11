from anyio import fail_after, to_thread
from fastapi import APIRouter, Depends, Request

from app.api.deps import verificar_api_key
from app.core.config import Settings, get_settings
from app.core.exceptions import ProcessamentoExpirou
from app.core.limiter import limiter
from app.schemas.texto import EntradaTexto, SaidaTexto
from app.services.transformacao import Transformador, get_transformador

router = APIRouter(prefix="/api/v1", tags=["transformacao"])


@router.post(
    "/transformar",
    response_model=SaidaTexto,
    dependencies=[Depends(verificar_api_key)],
    responses={
        400: {"description": "JSON malformado"},
        401: {"description": "API key ausente ou inválida"},
        413: {"description": "Corpo muito grande"},
        422: {"description": "Entrada inválida"},
        429: {"description": "Limite de requisições excedido"},
        504: {"description": "Processamento excedeu o tempo limite"},
    },
)
@limiter.limit(lambda: get_settings().rate_limit)
async def transformar_texto(
    request: Request,  # exigido pelo slowapi
    entrada: EntradaTexto,
    settings: Settings = Depends(get_settings),
    transformador: Transformador = Depends(get_transformador),
) -> SaidaTexto:
    try:
        # A regra é síncrona: roda em thread para não bloquear o event loop.
        # abandon_on_cancel=True é obrigatório: sem ele o run_sync espera a thread terminar e o
        # timeout nunca dispara. Com ele, o cliente recebe 504 no prazo e a thread termina em segundo plano.
        with fail_after(settings.request_timeout_seconds):
            resultado = await to_thread.run_sync(transformador, entrada.texto, abandon_on_cancel=True)
    except TimeoutError as exc:
        raise ProcessamentoExpirou() from exc
    return SaidaTexto(resultado=resultado)
