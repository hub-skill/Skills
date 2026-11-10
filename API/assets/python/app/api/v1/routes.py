from anyio import fail_after, to_thread
from fastapi import APIRouter, Depends

from app.api.deps import verificar_api_key
from app.core.config import Settings, get_settings
from app.core.exceptions import ProcessamentoExpirou
from app.schemas.texto import EntradaTexto, SaidaTexto
from app.services.transformacao import transformar

router = APIRouter(prefix="/api/v1", tags=["transformacao"])


@router.post(
    "/transformar",
    response_model=SaidaTexto,
    dependencies=[Depends(verificar_api_key)],
    responses={
        401: {"description": "API key ausente ou inválida"},
        422: {"description": "Entrada inválida"},
        504: {"description": "Processamento excedeu o tempo limite"},
    },
)
async def transformar_texto(
    entrada: EntradaTexto,
    settings: Settings = Depends(get_settings),
) -> SaidaTexto:
    try:
        # transformar é síncrona: roda em thread para não bloquear o event loop.
        # A thread não é cancelada no timeout; o cliente recebe 504 e o trabalho termina em segundo plano.
        with fail_after(settings.request_timeout_seconds):
            resultado = await to_thread.run_sync(transformar, entrada.texto)
    except TimeoutError as exc:
        raise ProcessamentoExpirou() from exc
    return SaidaTexto(resultado=resultado)
