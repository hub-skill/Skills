using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProjetoApi.Bancos;
using Xunit;

namespace ProjetoApi.UnitTests;

public sealed class RespostaDeSaudeComBancoTests
{
    private static async Task<string> EscreverAsync(HealthStatus status, string banco)
    {
        var contexto = new DefaultHttpContext();
        contexto.Response.Body = new MemoryStream();
        var relatorio = new HealthReport(
            new Dictionary<string, HealthReportEntry> { ["banco"] = new(status, null, TimeSpan.Zero, null, null) },
            TimeSpan.Zero);

        await RespostaDeSaudeComBanco.Pronto(contexto, relatorio, banco);

        contexto.Response.Body.Position = 0;
        return await new StreamReader(contexto.Response.Body).ReadToEndAsync();
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("oracle")]
    public async Task Banco_saudavel_responde_ok_com_o_nome_do_banco(string banco) =>
        Assert.Equal($$"""{"status":"ok","banco":"{{banco}}"}""", await EscreverAsync(HealthStatus.Healthy, banco));

    [Theory]
    [InlineData(HealthStatus.Unhealthy)]
    [InlineData(HealthStatus.Degraded)]
    public async Task Banco_fora_ou_degradado_responde_banco_indisponivel(HealthStatus status) =>
        Assert.Equal("""{"status":"banco_indisponivel"}""", await EscreverAsync(status, "postgres"));
}
