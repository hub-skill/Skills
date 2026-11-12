using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ProjetoApi.Bancos;

/// <summary>
/// Corpo do <c>/health/ready</c> de uma API com banco: pronto = 200 <c>{"status":"ok","banco":"postgres"}</c>; com o banco
/// fora (ou degradado) = 503 <c>{"status":"banco_indisponivel"}</c>, o mesmo nome do <c>code</c> de erro das chamadas de
/// negócio. O status HTTP (200/503) continua vindo do middleware de health checks. Substitui
/// <c>RespostaDeSaude.Pronto</c> do template base, que é genérico (serve a API sem banco).
/// </summary>
public static class RespostaDeSaudeComBanco
{
    public static Task Pronto(HttpContext contexto, HealthReport relatorio, string banco)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(relatorio);
        return relatorio.Status == HealthStatus.Healthy
            ? contexto.Response.WriteAsJsonAsync(new { status = "ok", banco })
            : contexto.Response.WriteAsJsonAsync(new { status = "banco_indisponivel" });
    }
}
