import logging
import uuid

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.middleware.cors import CORSMiddleware
from fastapi.middleware.trustedhost import TrustedHostMiddleware
from fastapi.responses import JSONResponse
from starlette.exceptions import HTTPException as StarletteHTTPException

from app.api.v1.routes import router as router_v1
from app.core.config import get_settings
from app.core.exceptions import ErroDeDominio

logger = logging.getLogger(__name__)
settings = get_settings()

TAMANHO_MAXIMO_CORPO = 64 * 1024  # bytes; o proxy deve aplicar um limite também

app = FastAPI(
    title="API de transformação de texto",
    version="1.0.0",
    docs_url="/docs" if settings.enable_docs else None,
    redoc_url="/redoc" if settings.enable_docs else None,
    openapi_url="/openapi.json" if settings.enable_docs else None,
)

app.add_middleware(TrustedHostMiddleware, allowed_hosts=settings.allowed_hosts)
app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.allowed_origins,
    allow_methods=["POST"],
    allow_headers=["Content-Type", "X-API-Key"],
)


def _resposta_erro(
    request: Request,
    status_code: int,
    codigo: str,
    mensagem: str,
    headers: dict[str, str] | None = None,
) -> JSONResponse:
    return JSONResponse(
        status_code=status_code,
        content={
            "error": {
                "code": codigo,
                "message": mensagem,
                "request_id": getattr(request.state, "request_id", None),
            }
        },
        headers=headers,
    )


@app.middleware("http")
async def contexto_da_requisicao(request: Request, call_next):
    request.state.request_id = str(uuid.uuid4())

    tamanho = request.headers.get("content-length")
    if tamanho is not None and tamanho.isdigit() and int(tamanho) > TAMANHO_MAXIMO_CORPO:
        return _resposta_erro(request, 413, "corpo_muito_grande", "Corpo da requisição excede o limite.")

    response = await call_next(request)
    response.headers["X-Request-ID"] = request.state.request_id
    response.headers["X-Content-Type-Options"] = "nosniff"
    response.headers["X-Frame-Options"] = "DENY"
    response.headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains"
    return response


@app.exception_handler(ErroDeDominio)
async def tratar_erro_de_dominio(request: Request, exc: ErroDeDominio) -> JSONResponse:
    return _resposta_erro(request, exc.status_code, exc.codigo, exc.mensagem)


@app.exception_handler(RequestValidationError)
async def tratar_erro_de_validacao(request: Request, exc: RequestValidationError) -> JSONResponse:
    # Mensagem genérica: os detalhes do Pydantic podem ecoar a entrada.
    return _resposta_erro(request, 422, "entrada_invalida", "Dados de entrada inválidos.")


@app.exception_handler(StarletteHTTPException)
async def tratar_erro_http(request: Request, exc: StarletteHTTPException) -> JSONResponse:
    return _resposta_erro(
        request, exc.status_code, f"http_{exc.status_code}", str(exc.detail), headers=exc.headers
    )


@app.exception_handler(Exception)
async def tratar_erro_inesperado(request: Request, exc: Exception) -> JSONResponse:
    logger.exception("erro não tratado", extra={"request_id": getattr(request.state, "request_id", None)})
    return _resposta_erro(request, 500, "erro_interno", "Erro interno. Informe o request_id ao suporte.")


@app.get("/health/live", include_in_schema=False)
async def live() -> dict[str, str]:
    return {"status": "ok"}


app.include_router(router_v1)
