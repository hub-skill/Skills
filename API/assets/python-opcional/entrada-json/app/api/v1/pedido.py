"""Rota com entrada JSON estruturada. Inclua em main.py: ``app.include_router(router_pedido)``."""

from fastapi import APIRouter, Depends, Request

from app.api.deps import verificar_api_key
from app.core.config import get_settings
from app.core.limiter import limiter
from app.schemas.pedido import EntradaPedido, SaidaPedido
from app.services.pedido import totalizar

router_pedido = APIRouter(prefix="/api/v1", tags=["pedidos"])


@router_pedido.post(
    "/pedidos",
    response_model=SaidaPedido,
    dependencies=[Depends(verificar_api_key)],
    responses={
        400: {"description": "Corpo ilegível: JSON malformado ou aninhado fundo demais"},
        401: {"description": "API key ausente ou inválida"},
        413: {"description": "Corpo muito grande"},
        422: {"description": "JSON válido com forma errada (campo desconhecido, tipo errado, limite)"},
        429: {"description": "Limite de requisições excedido"},
    },
)
@limiter.limit(lambda: get_settings().rate_limit)
async def criar_pedido(request: Request, entrada: EntradaPedido) -> SaidaPedido:
    # request: exigido pelo slowapi. Cálculo curto e limitado (até 100 itens): roda direto.
    # Se a regra for lenta, use to_thread + fail_after como em routes.py.
    return totalizar(entrada)
