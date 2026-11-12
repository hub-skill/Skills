## Parte 4: Equivalência entre Python e C#

| Conceito | Python (FastAPI) | C# (ASP.NET Core) |
|---|---|---|
| Configuração tipada | `pydantic-settings` | `IOptions<T>` + DataAnnotations |
| Segredo | `SecretStr` | Variável de ambiente e cuidado ao logar |
| Validação de entrada | `Field` + `field_validator` | DataAnnotations + FluentValidation |
| Comparação de chave | `secrets.compare_digest` | `CryptographicOperations.FixedTimeEquals` |
| Autenticação | Dependência (`Depends`) | `AuthenticationHandler<T>` + `RequireAuthorization` |
| Rate limit | `slowapi` | `AddRateLimiter` (nativo) |
| Limite de concorrência | `anyio.CapacityLimiter` | `ConcurrencyLimiter` ou `SemaphoreSlim` |
| Timeout | `anyio.fail_after` | `AddRequestTimeouts` ou `CancellationToken` |
| Trabalho CPU-bound | `to_thread.run_sync` | `Task.Run` |
| Formato de erro (o mesmo corpo RFC 9457 nos dois) | Handlers + `_resposta_erro` | `RespostaDeErro` + `IExceptionHandler` + `UseStatusCodePages` |
| Hosts confiáveis | `TrustedHostMiddleware` | `AddHostFiltering` |
| CORS | `CORSMiddleware` | `AddCors` |
| Logs JSON | `logging` com formatador JSON | `AddJsonConsole` |
| Métricas | `prometheus-fastapi-instrumentator` | OpenTelemetry + exportador Prometheus |
| Health checks (mesmo JSON nos dois) | Rotas próprias | `MapHealthChecks` + `ResponseWriter` |
| Documentação | OpenAPI automático + `/docs` | `AddOpenApi` + Scalar |
| Testes de integração | `httpx` + `ASGITransport` | `WebApplicationFactory` |
| Testes de propriedade | hypothesis | FsCheck |
| Lint e formatação | ruff | `dotnet format` + analyzers |
| Análise de segurança | bandit | Analyzers do SDK + SecurityCodeScan |
| Auditoria de dependências | pip-audit | `dotnet list package --vulnerable` |
| Versões fixas | `uv.lock` | `global.json` + `Directory.Packages.props` |
| Servidor de produção | Gunicorn + UvicornWorker | Kestrel (sem processo gerenciador adicional) |
| Imagem base | `python:3.12-slim` | `mcr.microsoft.com/dotnet/aspnet:10.0` |
| Usuário não-root | `useradd` no Dockerfile | Usuário `app` já incluso na imagem |

---

