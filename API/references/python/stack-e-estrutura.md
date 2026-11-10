## Parte 2: Python (FastAPI)

### 2.1 Stack

| Finalidade | Componente / biblioteca | Observação |
|---|---|---|
| Framework HTTP | `fastapi` | Validação e OpenAPI integrados |
| Servidor ASGI | `uvicorn` | Executado como worker do Gunicorn |
| Gerenciador de processos | `gunicorn` + `uvicorn-worker` | Em versões antigas, `uvicorn.workers.UvicornWorker` |
| Validação de dados | `pydantic` (incluso no FastAPI) | `Field`, `field_validator`, `SecretStr` |
| Configuração | `pydantic-settings` | Lê variáveis de ambiente e `.env` |
| Autenticação | Dependência própria com `secrets.compare_digest` | Sem biblioteca externa para chave simples |
| Rate limit | `slowapi` | Backend Redis quando houver mais de uma instância |
| Limite de concorrência | `anyio.CapacityLimiter` | Já incluso no `anyio` |
| Timeout | `anyio.fail_after` | Retorna 504 via exceção de domínio |
| Cliente HTTP | `httpx` | Também usado nos testes com `ASGITransport` |
| Testes | `pytest`, `pytest-asyncio`, `pytest-cov` | `asyncio_mode = "auto"` |
| Propriedades | `hypothesis` | Gera entradas variadas |
| Contrato | `schemathesis` | Fuzzing a partir do OpenAPI |
| Lint e formatação | `ruff` | Substitui flake8, isort e black |
| Tipos | `mypy` (ou `pyright`) | Modo estrito |
| Segurança estática | `bandit` | Análise do código |
| Dependências | `pip-audit` | Vulnerabilidades conhecidas |
| Métricas | `prometheus-fastapi-instrumentator` | Expõe `/metrics` |
| Gerenciador de pacotes | `uv` (ou `poetry`) | Gera lock file |

### 2.2 Estrutura de pastas

```
projeto/
├── app/
│   ├── main.py               # app, middlewares, handlers de erro, routers
│   ├── core/
│   │   ├── config.py         # Settings (pydantic-settings)
│   │   ├── exceptions.py     # exceções de domínio
│   │   └── logging.py        # logs JSON
│   ├── api/
│   │   ├── deps.py           # verificação de API key
│   │   └── v1/routes.py      # endpoints /api/v1
│   ├── schemas/
│   │   └── texto.py          # EntradaTexto, SaidaTexto
│   ├── services/
│   │   └── transformacao.py  # regra de negócio (função pura)
│   └── middleware/
│       └── seguranca.py      # headers, limite de corpo
├── tests/
│   ├── unit/
│   ├── integration/
│   └── conftest.py
├── .env.example
├── pyproject.toml            # dependências, ruff, mypy, pytest
├── uv.lock                   # versões fixas
├── Dockerfile
└── README.md
```

### 2.3 Componentes principais

**Configuração (`core/config.py`)**
- Classe `Settings` com `BaseSettings`.
- `api_key: SecretStr` sem valor padrão, então a aplicação não sobe sem ela.
- `allowed_origins`, `allowed_hosts`, `request_timeout_seconds`, `max_concurrent_jobs`, `enable_docs`.
- Acesso por função com `lru_cache`.

**Schema de entrada (`schemas/texto.py`)**
- `texto: str` com `min_length=1` e `max_length=10000`.
- `field_validator` que faz `strip()`, rejeita vazio, rejeita caracteres de controle com regex de tamanho fixo.

**Autenticação (`api/deps.py`)**
- Dependência que lê `X-API-Key` com `APIKeyHeader(auto_error=False)`.
- Compara com `secrets.compare_digest`.
- Retorna 401 com `WWW-Authenticate`.

**Serviço (`services/transformacao.py`)**
- Função pura, sem I/O e sem estado global.

**Rota (`api/v1/routes.py`)**
- `POST /api/v1/transformar`, com `response_model`, `dependencies` de autenticação e `responses` documentadas.
- Processamento dentro de `anyio.fail_after(settings.request_timeout_seconds)`, executado com `to_thread.run_sync`.
- `TimeoutError` convertido em exceção de domínio com status 504.

**Aplicação (`main.py`)**
- `FastAPI` com `docs_url`, `redoc_url` e `openapi_url` condicionados a `enable_docs`.
- `TrustedHostMiddleware` e `CORSMiddleware` com origens explícitas.
- Middleware HTTP que gera `request_id`, verifica `Content-Length` (413) e adiciona headers de segurança.
- Handlers para `ErroDeDominio`, `RequestValidationError`, `StarletteHTTPException` e `Exception`, todos no mesmo formato de erro.

**Observabilidade**
- `prometheus-fastapi-instrumentator` expondo `/metrics` na rede interna.
- Logs JSON com `request_id`.
- `/health/live` (não verifica dependências) e `/health/ready` (verifica dependências).

**Testes (`tests/`)**
- Unitários do serviço com pytest.
- Integração com `httpx.AsyncClient(transport=ASGITransport(app=app))`.
- Variáveis de ambiente definidas antes da importação da aplicação.

### 2.4 Execução e deploy

- Gunicorn com `-k uvicorn_worker.UvicornWorker`, `-w` configurável, `--timeout`, `--graceful-timeout` e `--keep-alive`.
- Dockerfile multi-stage: builder com dependências, imagem final `python:3.12-slim` com usuário não-root.
- Imagem base com tag fixa, não `latest`.
- `.env.example` documentando todas as variáveis.

### 2.5 Pipeline de CI

1. `ruff check` e `ruff format --check`
2. `mypy app`
3. `pytest --cov`
4. `bandit -r app`
5. `pip-audit`
6. `docker build` e `trivy image`

---

