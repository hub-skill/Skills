## Parte 3: C# (ASP.NET Core)

### 3.1 Stack

| Finalidade | Componente / biblioteca | Observação |
|---|---|---|
| Framework HTTP | ASP.NET Core Minimal APIs | Incluso no SDK |
| Runtime | .NET 10 (LTS) | Fixar via `global.json` |
| Configuração | `Microsoft.Extensions.Configuration` + Options | `IOptions<T>` com `ValidateDataAnnotations` e `ValidateOnStart` |
| Validação de entrada | DataAnnotations para limites; `FluentValidation.AspNetCore` para regras de conteúdo | Caracteres de controle e strip ficam no validador |
| Autenticação | `Microsoft.AspNetCore.Authentication` com `AuthenticationHandler<T>` próprio | Comparação com `CryptographicOperations.FixedTimeEquals` |
| Autorização | `AddAuthorization` com política `RequireAuthenticatedUser` | Políticas por rota |
| Rate limit | `Microsoft.AspNetCore.RateLimiting` (incluso) | `FixedWindowLimiter` ou `TokenBucketLimiter`; `PartitionedRateLimiter` por chave |
| Timeout de requisição | `Microsoft.AspNetCore.Http.Timeouts` (incluso) | `AddRequestTimeouts`; retorna 504 por padrão |
| Limite de concorrência | `RateLimiter` com `ConcurrencyLimiter` ou `SemaphoreSlim` | Sem fila infinita |
| Erros | `RespostaDeErro` + `IExceptionHandler` (incluso) | RFC 9457, `application/problem+json`, só `title`/`status`/`code`/`request_id` (igual ao Python) |
| CORS | `AddCors` (incluso) | Origens explícitas |
| Filtro de hosts | `AddHostFiltering` (incluso) | Equivalente ao TrustedHost |
| Headers de segurança | Middleware próprio ou `NetEscapades.AspNetCore.SecurityHeaders` | HSTS, nosniff, X-Frame-Options |
| Limite de corpo | `[RequestSizeLimit]` ou `MaxRequestBodySize` no Kestrel | Configurável por rota |
| Logs | `ILogger` com `AddJsonConsole` (incluso) | `Serilog` opcional |
| Métricas e tracing | `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Exporter.Prometheus.AspNetCore` | Exporta `/metrics` |
| Health checks | `Microsoft.Extensions.Diagnostics.HealthChecks` (incluso) | `MapHealthChecks` com predicado para live e ready |
| Documentação | `Microsoft.AspNetCore.OpenApi` (pacote NuGet) + `Scalar.AspNetCore` | Swagger UI via Swashbuckle é alternativa |
| Versionamento | `Asp.Versioning.Http` | Versão na rota |
| Serialização | `System.Text.Json` | Source generators para desempenho |
| Testes unitários | xUnit | Padrão da comunidade |
| Testes de integração | `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`) | Sobe a aplicação em memória |
| Asserções | Shouldly ou asserts do xUnit | Evitar FluentAssertions pela mudança de licença |
| Propriedades | `FsCheck.Xunit` | Equivalente ao hypothesis |
| Cobertura | `coverlet.collector` | Limite no CI |
| Contrato | Schemathesis | Independe da linguagem |
| Carga | k6 | Independe da linguagem |
| Análise estática | Analyzers do SDK (`EnableNETAnalyzers`, `AnalysisLevel`) + `SecurityCodeScan.VS2019` opcional | Substitui bandit |
| Dependências | `dotnet list package --vulnerable` | Substitui pip-audit |
| Formatação | `dotnet format` com `.editorconfig` | Substitui ruff format |

### 3.2 Estrutura de pastas

```
ProjetoApi/
├── src/
│   └── ProjetoApi/
│       ├── Program.cs
│       ├── Configuration/
│       │   └── ApiSettings.cs
│       ├── Endpoints/
│       │   └── V1/TransformacaoEndpoints.cs
│       ├── Contracts/
│       │   ├── EntradaTexto.cs
│       │   └── SaidaTexto.cs
│       ├── Validation/
│       │   └── EntradaTextoValidator.cs
│       ├── Security/
│       │   ├── ApiKeyAuthenticationHandler.cs
│       │   └── ApiKeyDefaults.cs
│       ├── Errors/
│       │   ├── ErroDeDominioException.cs
│       │   └── ErroDeDominioHandler.cs
│       ├── Services/
│       │   ├── ITransformacaoService.cs
│       │   └── TransformacaoService.cs
│       ├── Middleware/
│       │   └── SecurityHeadersMiddleware.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       └── Dockerfile
├── tests/
│   ├── ProjetoApi.UnitTests/
│   └── ProjetoApi.IntegrationTests/
├── Directory.Build.props        # nullable, warnings como erro, analyzers
├── Directory.Packages.props     # Central Package Management: versões fixas
├── global.json                  # versão do SDK fixada
├── .editorconfig
├── ProjetoApi.sln
└── README.md
```

### 3.3 Componentes principais

**Projeto e build**
- `global.json` fixa o SDK. Sem isso, máquinas diferentes compilam com versões diferentes.
- `Directory.Build.props` define `Nullable`, `TreatWarningsAsErrors`, `AnalysisLevel` e `EnforceCodeStyleInBuild`.
- `Directory.Packages.props` (Central Package Management) concentra todas as versões de pacote em um lugar, o equivalente ao lock de versões do Python.

**Configuração (`Configuration/ApiSettings.cs`)**
- Classe com atributos de DataAnnotations (`[Required]`, `[Range]`).
- Registrada com `builder.Services.AddOptions<ApiSettings>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`, então a aplicação não sobe com configuração inválida.
- Chave de API lida como string e nunca logada. Não há `SecretStr` no .NET, então o cuidado é não logar o objeto de configuração.

**Contrato (`Contracts/`)**
- `record EntradaTexto(string Texto)` e `record SaidaTexto(string Resultado)`. Records são imutáveis e serializam bem.

**Validação (`Validation/EntradaTextoValidator.cs`)**
- Validador FluentValidation com `NotEmpty`, `MaximumLength(10000)`, `Must` para caracteres de controle (regex com `[GeneratedRegex]` e tamanho limitado).
- Para remover espaços, use um endpoint filter ou normalize no construtor do serviço, antes da validação de conteúdo. O endpoint filter fica registrado na rota.

**Autenticação (`Security/`)**
- `ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>`.
- Lê `X-API-Key`, compara com `CryptographicOperations.FixedTimeEquals` sobre bytes UTF-8.
- Registrada com `AddAuthentication(...).AddScheme<...>(...)` e `AddAuthorization`.
- Falha retorna 401 pelo pipeline padrão, que passa pelo handler de erros.

**Serviço (`Services/`)**
- `ITransformacaoService` com `Task<string> TransformarAsync(string texto, CancellationToken ct)`.
- Implementação pura, sem dependências de HTTP. Registrada como `Singleton` ou `Scoped` conforme o estado.
- Trabalho CPU-bound fora da thread de requisição com `Task.Run`, com cancelamento via `CancellationToken`.

**Endpoint (`Endpoints/V1/TransformacaoEndpoints.cs`)**
- Método de extensão `MapTransformacaoV1(this IEndpointRouteBuilder app)` que agrupa rotas em `/api/v1`.
- `MapPost("/transformar", ...)` com `.RequireAuthorization()`, `.WithOpenApi()`, `.Produces<SaidaTexto>(200)` e `.ProducesProblem(...)`.
- Timeout com `WithRequestTimeout(...)` quando a política for por rota, ou `CancellationTokenSource.CancelAfter` dentro do serviço.
- Retorno tipado com `Results<Ok<SaidaTexto>, ProblemHttpResult>`.

**Erros (`Errors/`)**
- `ErroDeDominioException : Exception` com status e código, como no Python.
- `RespostaDeErro` escreve todo erro: RFC 9457 com `application/problem+json` e exatamente `title`, `status`, `code` e `request_id` (o mesmo corpo do template Python). Títulos em português para 401/404/405.
- `IExceptionHandler` converte `ErroDeDominioException`, `BadHttpRequestException` (400) e exceções inesperadas usando `RespostaDeErro`.
- Erros do framework (401, 404, 405) saem por `UseStatusCodePages(ctx => RespostaDeErro.EscreverGenericoAsync(...))` e o 429 por `OnRejected` (`limite_excedido`). `AddProblemDetails()` continua registrado só porque o `UseExceptionHandler` exige; o `ProblemDetailsService` acrescentaria `type` e `traceId`.
- `RouteHandlerOptions.ThrowOnBadRequest = true`: o padrão só lança em Development; em produção o JSON malformado sairia 400 fora do formato.
- Exceção inesperada: log com stack trace, resposta 500 genérica.

**Rate limit e concorrência (`Program.cs`)**
- `AddRateLimiter` com `PartitionedRateLimiter.Create` por chave de API ou IP.
- Resposta 429 com `Retry-After` e corpo `limite_excedido` configurados em `OnRejected`.
- `ConcurrencyLimiter` para limitar processamentos simultâneos.
- Em mais de uma instância, o rate limiter nativo é por processo. Para limite global, use um gateway ou Redis via biblioteca dedicada.

**Segurança e rede (`Program.cs`)**
- `AddCors` com `WithOrigins(...)` vindo da configuração.
- `AddHostFiltering` com a lista de hosts.
- `UseForwardedHeaders` configurado apenas com proxies confiáveis, para que o IP usado no rate limit não seja forjado.
- `MaxRequestBodySize` no Kestrel e `[RequestSizeLimit]` nas rotas de entrada.
- Middleware de headers de segurança ou pacote `NetEscapades`.

**Documentação (`Program.cs`)**
- `AddOpenApi()` e `MapOpenApi()` apenas quando `EnableDocs` for verdadeiro. `MapScalarApiReference()` para a interface.

**Observabilidade**
- `AddJsonConsole` para logs em JSON com `TraceIdentifier`.
- OpenTelemetry com `AddAspNetCoreInstrumentation()` e exportador Prometheus em `/metrics`, restrito à rede interna.
- `MapHealthChecks("/health/live", ...)` com `Predicate = _ => false` e `MapHealthChecks("/health/ready", ...)` com checks registrados, os dois com `ResponseWriter` de `RespostaDeSaude` (JSON `{"status":"ok"}`, igual ao Python) e `Degraded` = 503.

**Testes**
- `ProjetoApi.UnitTests`: xUnit para o serviço e para o validador.
- `ProjetoApi.IntegrationTests`: `WebApplicationFactory<Program>` com `UseEnvironment("Production")` (o padrão é Development, que esconde diferenças como o `ThrowOnBadRequest`) e `UseSetting` para a chave de teste. Cobre os mesmos casos do Python; `ContratoDeErro.AfirmarProblema` confere o formato do erro igual ao `tests/contrato.py`.
- `ComportamentoDaApiTests`: testes genéricos de comportamento (formato de erro, 401/404/413/429/500/504, health, cabeçalhos, documentação). `ApiFactoryConfiguravel` sobe a API com outra configuração e, se preciso, com um serviço falso (`ServicoFalso`) que atrasa ou lança exceção. O TestServer não aplica `MaxRequestBodySize` do Kestrel: o 413 vem de um middleware que confere o `Content-Length`.
- `Program` precisa ficar acessível aos testes (classe `public partial class Program { }` ao final de `Program.cs`).
- Propriedades com `FsCheck.Xunit` (`[Property]`).
- `coverlet.collector` para cobertura.

### 3.4 Execução e deploy

- Dockerfile multi-stage: `mcr.microsoft.com/dotnet/sdk:10.0` para build e `mcr.microsoft.com/dotnet/aspnet:10.0` para execução, com tag fixa.
- A imagem `aspnet` já traz usuário não-root (`app`, UID 1654). Usar `USER $APP_UID` em vez de criar um usuário.
- Kestrel escuta na porta 8080 dentro do container. Ajustar `ASPNETCORE_HTTP_PORTS` se necessário.
- `ShutdownTimeout` em `HostOptions` para graceful shutdown.
- `appsettings.{Environment}.json` por ambiente, com segredos vindo de variáveis de ambiente ou de um secret manager.
- Publicação com `dotnet publish -c Release`. Para imagens menores, `PublishTrimmed` e `PublishAot` exigem testes adicionais, porque reflexão e serialização podem quebrar.

### 3.5 Pipeline de CI

1. `dotnet format --verify-no-changes`
2. `dotnet build -c Release` (com warnings como erro)
3. `dotnet test --collect:"XPlat Code Coverage"` com limite de cobertura
4. `dotnet list package --vulnerable --include-transitive` (falha se encontrar vulnerabilidade)
5. Análise de segurança com os analyzers do SDK e, se adotado, `SecurityCodeScan`
6. `docker build` e `trivy image`

---

