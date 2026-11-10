using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using ProjetoApi.Configuration;
using ProjetoApi.Contracts;
using ProjetoApi.Endpoints.V1;
using ProjetoApi.Errors;
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

builder.Logging.AddJsonConsole();
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 64 * 1024);

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
    opcoes.OnRejected = (contexto, _) =>
    {
        contexto.HttpContext.Response.Headers["Retry-After"] = "60";
        return ValueTask.CompletedTask;
    };
    opcoes.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
        RateLimitPartition.GetFixedWindowLimiter(
            contexto.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Erros padronizados (RFC 7807)
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErroDeDominioHandler>();

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
app.UseHostFiltering();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseForwardedHeaders();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Documentação apenas quando habilitada (desligada em produção por padrão).
if (settings.EnableDocs)
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Health checks anônimos; métricas devem ficar na rede interna.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapPrometheusScrapingEndpoint("/metrics").AllowAnonymous();

app.MapTransformacaoV1();

app.Run();

// Torna a classe Program acessível aos testes de integração.
public partial class Program { }
