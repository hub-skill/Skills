import logging
import uuid
from collections.abc import Awaitable, Callable

from fastapi import FastAPI, Request, Response
from fastapi.exceptions import RequestValidationError
from fastapi.middleware.cors import CORSMiddleware
from fastapi.middleware.trustedhost import TrustedHostMiddleware
from fastapi.responses import JSONResponse
from prometheus_fastapi_instrumentator import Instrumentator
from slowapi.errors import RateLimitExceeded
from starlette.exceptions import HTTPException as StarletteHTTPException

from app.api.deps import chave_valida
from app.api.v1.routes import router as router_v1
from app.core.config import get_settings
from app.core.exceptions import ErroDeDominio
from app.core.limiter import limiter
from app.core.logging import configurar_logs
from app.core.profundidade_json import corpo_json_ilegivel

configurar_logs()
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
app.state.limiter = limiter

app.add_middleware(TrustedHostMiddleware, allowed_hosts=settings.allowed_hosts)
app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.allowed_origins,
    allow_methods=["POST"],
    allow_headers=["Content-Type", "X-API-Key"],
)


# Textos dos erros genéricos do framework (401, 404, 405), em português e iguais aos do template .NET.
_TITULOS_HTTP = {
    401: "Chave de API ausente ou inválida.",
    404: "Recurso não encontrado.",
    405: "Método não permitido.",
}


def _resposta_erro(
    request: Request,
    status_code: int,
    codigo: str,
    mensagem: str,
    headers: dict[str, str] | None = None,
) -> JSONResponse:
    """Formato único de erro: RFC 9457 (Problem Details), o mesmo do template .NET.

    Corpo sempre com exatamente `title`, `status`, `code` e `request_id` (sem `type`: ausente = about:blank).
    """
    return JSONResponse(
        status_code=status_code,
        content={
            "title": mensagem,
            "status": status_code,
            "code": codigo,
            "request_id": getattr(request.state, "request_id", None),
        },
        headers=headers,
        media_type="application/problem+json",
    )


@app.middleware("http")
async def contexto_da_requisicao(
    request: Request, call_next: Callable[[Request], Awaitable[Response]]
) -> Response:
    request.state.request_id = str(uuid.uuid4())

    tamanho = request.headers.get("content-length")
    grande = tamanho is not None and tamanho.isdigit() and int(tamanho) > TAMANHO_MAXIMO_CORPO
    # Só com chave válida a API lê o corpo aqui (sem ela a resposta é 401). O corpo fica em cache para a rota.
    chave_ok = chave_valida(request.headers.get("X-API-Key"), get_settings())
    corpo = b"" if grande or not chave_ok else await request.body()

    if grande:
        response: Response = _resposta_erro(
            request, 413, "corpo_muito_grande", "Corpo da requisição excede o limite."
        )
    elif (
        # JSON que o template .NET recusa na leitura (mais de 64 níveis, NaN/Infinity): 400, igual a ele.
        chave_ok
        and request.method == "POST"
        and "json" in request.headers.get("content-type", "")
        and corpo_json_ilegivel(corpo)
    ):
        response = _resposta_erro(request, 400, "requisicao_invalida", "Requisição inválida.")
    else:
        try:
            response = await call_next(request)
        except Exception as exc:  # noqa: BLE001 - o 500 sai daqui para receber os mesmos headers das demais respostas
            response = await tratar_erro_inesperado(request, exc)

    # Toda resposta, de sucesso ou de erro (inclusive 413, 400 e 500), leva request_id e headers de segurança.
    response.headers["X-Request-ID"] = request.state.request_id
    response.headers["X-Content-Type-Options"] = "nosniff"
    response.headers["X-Frame-Options"] = "DENY"
    response.headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains"
    # Respostas (e erros, com request_id) não podem ficar em cache de proxy ou navegador.
    response.headers["Cache-Control"] = "no-store"
    return response


@app.exception_handler(ErroDeDominio)
async def tratar_erro_de_dominio(request: Request, exc: ErroDeDominio) -> JSONResponse:
    return _resposta_erro(request, exc.status_code, exc.codigo, exc.mensagem)


@app.exception_handler(RequestValidationError)
async def tratar_erro_de_validacao(request: Request, exc: RequestValidationError) -> JSONResponse:
    # JSON malformado é erro de requisição (400), não de conteúdo (422).
    if any(erro.get("type") == "json_invalid" for erro in exc.errors()):
        return _resposta_erro(request, 400, "requisicao_invalida", "Requisição inválida.")
    # Mensagem genérica: os detalhes do Pydantic podem ecoar a entrada.
    return _resposta_erro(request, 422, "entrada_invalida", "Dados de entrada inválidos.")


@app.exception_handler(RateLimitExceeded)
async def tratar_rate_limit(request: Request, exc: RateLimitExceeded) -> JSONResponse:
    return _resposta_erro(
        request,
        429,
        "limite_excedido",
        "Muitas requisições. Tente mais tarde.",
        headers={"Retry-After": "60"},
    )


@app.exception_handler(StarletteHTTPException)
async def tratar_erro_http(request: Request, exc: StarletteHTTPException) -> JSONResponse:
    # Rota inexistente ou método errado SEM chave válida: 401, como no template .NET
    # (a API não revela quais rotas existem).
    if exc.status_code in (404, 405) and not chave_valida(request.headers.get("X-API-Key"), get_settings()):
        return _resposta_erro(
            request, 401, "http_401", _TITULOS_HTTP[401], headers={"WWW-Authenticate": "APIKey"}
        )
    # Corpo que o FastAPI não consegue ler (ex.: UTF-8 inválido): mesmo erro do JSON malformado, em vez de
    # 400 http_400 com o texto do framework em inglês.
    if exc.status_code == 400:
        return _resposta_erro(request, 400, "requisicao_invalida", "Requisição inválida.")
    return _resposta_erro(
        request,
        exc.status_code,
        f"http_{exc.status_code}",
        _TITULOS_HTTP.get(exc.status_code, str(exc.detail)),
        headers=dict(exc.headers) if exc.headers else None,
    )


@app.exception_handler(Exception)
async def tratar_erro_inesperado(request: Request, exc: Exception) -> JSONResponse:
    # Stack trace só no log; o cliente recebe mensagem genérica, sem detalhes internos.
    logger.exception("erro não tratado", extra={"request_id": getattr(request.state, "request_id", None)})
    return _resposta_erro(request, 500, "erro_interno", "Erro interno. Informe o request_id ao suporte.")


@app.get("/health/live", include_in_schema=False)
async def live() -> dict[str, str]:
    return {"status": "ok"}


@app.get("/health/ready", include_in_schema=False)
async def ready() -> dict[str, str]:
    # Se a API tiver dependências (banco, serviços), verifique-as aqui e responda 503 quando falharem.
    return {"status": "ok"}


# Métricas Prometheus (latência, requisições, erros). Exponha /metrics só na rede interna.
Instrumentator().instrument(app).expose(app, endpoint="/metrics", include_in_schema=False)

app.include_router(router_v1)
