"""Corpo do /health/ready de uma API com banco.

Pronto: 200 ``{"status": "ok", "banco": "postgres"}``. Banco fora ou lento: 503
``{"status": "banco_indisponivel"}``, o mesmo nome do ``code`` de erro das chamadas de negócio.
O template base responde só ``{"status": "ok"}`` (serve à API sem banco).
"""

from collections.abc import Awaitable, Callable

from fastapi.responses import JSONResponse

from app.bancos.erros import BancoExpirou, BancoIndisponivel


async def resposta_pronto(verificar: Callable[[], Awaitable[object]], banco: str) -> JSONResponse:
    """Roda ``verificar`` (ex.: ``select 1`` em ``traduzir_erros_postgres()``) e monta a resposta."""
    try:
        await verificar()
    except (BancoIndisponivel, BancoExpirou):
        return JSONResponse({"status": "banco_indisponivel"}, status_code=503)
    return JSONResponse({"status": "ok", "banco": banco})
