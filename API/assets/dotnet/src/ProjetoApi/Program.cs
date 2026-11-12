using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using ProjetoApi.Configuration;
using ProjetoApi.Contracts;
using ProjetoApi.Endpoints.V1;
using ProjetoApi.Errors;
using ProjetoApi.Health;
using ProjetoApi.Middleware;
using ProjetoApi.Security;
using ProjetoApi.Services;
using ProjetoApi.Validation;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Configuração tipada e validada na inicialização.
builder.Services
    .AddOptions<ApiSettings>()
    .Bind(builder.Configuration.GetSection(ApiSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ApiSettings>>().Value);

var settings = builder.Configuration.GetSection(ApiSettings.SectionName).Get<ApiSettings>()!;

const long TamanhoMaximoDoCorpo = 64 * 1024; // o proxy deve limitar também

builder.Logging.AddJsonConsole();
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = TamanhoMaximoDoCorpo);

// Serviços e validação
builder.Services.AddSingleton<ITransformacaoService, TransformacaoService>();
builder.Services.AddValidatorsFromAssemblyContaining<EntradaTextoValidator>();

// Autenticação por API key. Por padrão, todas as rotas exigem autenticação.
builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(opcoes =>
    opcoes.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Rate limit por IP (use chave de API se houver clientes identificados).
builder.Services.AddRateLimiter(opcoes =>
{
    opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opcoes.OnRejected = async (contexto, ct) =>
    {
        contexto.HttpContext.Response.Headers.RetryAfter = "60";
        await RespostaDeErro.EscreverAsync(contexto.HttpContext, StatusCodes.Status429TooManyRequests,
            "limite_excedido", "Muitas requisições. Tente mais tarde.", ct);
    };
    opcoes.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
        RateLimitPartition.GetFixedWindowLimiter(
            contexto.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = settings.RateLimitPorMinuto,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Erros no formato único (RFC 9457, ver RespostaDeErro): title, status, code e request_id, inclusive 401, 404,
// 405 e 429. AddProblemDetails fica só porque o UseExceptionHandler exige; quem escreve o corpo é RespostaDeErro
// (o ProblemDetailsService acrescentaria "type" e "traceId").
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErroDeDominioHandler>();
// JSON malformado vira BadHttpRequestException (400 requisicao_invalida) em qualquer ambiente. O padrão do
// framework é lançar só em Development; em produção responderia 400 fora do nosso formato.
builder.Services.Configure<RouteHandlerOptions>(opcoes => opcoes.ThrowOnBadRequest = true);

// Proxies: adicione os IPs confiáveis em KnownProxies. Sem isso, X-Forwarded-* é ignorado.
builder.Services.Configure<ForwardedHeadersOptions>(opcoes =>
    opcoes.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

// CORS e filtro de hosts
builder.Services.AddCors(opcoes => opcoes.AddDefaultPolicy(politica =>
    politica.WithOrigins(settings.AllowedOrigins)
            .WithMethods("POST")
            .WithHeaders("Content-Type", ApiKeyAuthenticationHandler.Header)));
builder.Services.AddHostFiltering(opcoes => opcoes.AllowedHosts = settings.AllowedHosts);

// Observabilidade e documentação
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddOpenTelemetry()
    .WithMetrics(metricas => metricas
        .AddAspNetCoreInstrumentation()
        .AddPrometheusExporter());

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages(contexto => RespostaDeErro.EscreverGenericoAsync(contexto.HttpContext)); // 401, 404, 405
app.UseHostFiltering();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.Use(async (contexto, proximo) =>
{
    // O Kestrel também limita; esta checagem responde 413 no formato padrão de erro
    // (e vale no TestServer dos testes, que não aplica o limite do Kestrel).
    if (contexto.Request.ContentLength > TamanhoMaximoDoCorpo)
    {
        throw new CorpoMuitoGrandeException();
    }

    await proximo(contexto);
});
app.UseForwardedHeaders();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Documentação apenas quando habilitada (desligada em produção por padrão).
if (settings.EnableDocs)
{
    // AllowAnonymous: sem isso a política padrão (autenticação obrigatória) bloqueia a documentação com 401.
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

// Health checks anônimos; métricas devem ficar na rede interna.
// Fora do rate limit: no Kubernetes as probes saem todas do IP do nó; um 429 na liveness reiniciaria o pod.
// Corpo JSON igual ao do template Python (RespostaDeSaude); Degraded também conta como não pronto (503).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = RespostaDeSaude.Vivo })
    .AllowAnonymous().DisableRateLimiting();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResponseWriter = RespostaDeSaude.Pronto,
    ResultStatusCodes = { [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable }
}).AllowAnonymous().DisableRateLimiting();
app.MapPrometheusScrapingEndpoint("/metrics").AllowAnonymous().DisableRateLimiting();

app.MapTransformacaoV1();

app.Run();

// Torna a classe Program acessível aos testes de integração.
public partial class Program { }
