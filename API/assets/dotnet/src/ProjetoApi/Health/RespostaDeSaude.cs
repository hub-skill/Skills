using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ProjetoApi.Health;

/// <summary>
/// Corpo JSON dos health checks, o mesmo do template Python:
/// /health/live -> {"status":"ok"}; /health/ready -> {"status":"ok"} ou 503 {"status":"indisponivel"}.
/// O status HTTP (200/503) continua vindo do middleware de health checks.
/// </summary>
public static class RespostaDeSaude
{
    public static Task Vivo(HttpContext contexto, HealthReport relatorio) =>
        contexto.Response.WriteAsJsonAsync(new { status = "ok" });

    public static Task Pronto(HttpContext contexto, HealthReport relatorio)
    {
        ArgumentNullException.ThrowIfNull(relatorio);
        return relatorio.Status == HealthStatus.Healthy
            ? contexto.Response.WriteAsJsonAsync(new { status = "ok" })
            : contexto.Response.WriteAsJsonAsync(new { status = "indisponivel" });
    }
}
