using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ProjetoApi.Bancos;

/// <summary>
/// /health/ready: a API só está pronta se o banco responde. Recebe a verificação (ex.: "select 1"),
/// que deve passar por ErrosPostgres/ErrosOracle para lançar ErroDeBancoException quando falhar.
/// </summary>
public sealed class BancoHealthCheck(Func<CancellationToken, Task> verificar) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await verificar(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (ErroDeBancoException)
        {
            return HealthCheckResult.Unhealthy("banco indisponível");
        }
    }
}
