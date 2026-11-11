using Oracle.ManagedDataAccess.Client;

namespace ProjetoApi.Bancos;

public static class ConexoesOracle
{
    /// <summary>
    /// String de conexão do ODP.NET gerenciado. Use o banco plugável (ex.: localhost:1521/XEPDB1), nunca o CDB.
    /// O tempo limite de cada comando vai em OracleCommand.CommandTimeout.
    /// </summary>
    public static string Montar(string dataSource, string usuario, string senha) =>
        new OracleConnectionStringBuilder
        {
            DataSource = dataSource,
            UserID = usuario,
            Password = senha,
            Pooling = true,
            MaxPoolSize = 5,
            ConnectionTimeout = 5,
        }.ConnectionString;
}

/// <summary>Traduz as exceções do ODP.NET para BancoIndisponivel (503) e BancoExpirou (504).</summary>
public static class ErrosOracle
{
    // ORA-01013: comando cancelado pelo CommandTimeout.
    private const int Cancelado = 1013;

    // Login, instância, conexão perdida e listener. ORA-50201: o ODP.NET gerenciado embrulha as falhas de
    // rede (ex.: ORA-12541, sem listener) neste código; sem ele, um Oracle fora do ar viraria erro 500.
    private static readonly HashSet<int> Indisponivel =
        [1017, 1033, 1034, 1089, 3113, 3114, 3135, 12154, 12170, 12514, 12537, 12541, 12545, 28000, 50201];

    public static async Task<T> TraduzirAsync<T>(Func<Task<T>> operacao)
    {
        ArgumentNullException.ThrowIfNull(operacao);
        try
        {
            return await operacao().ConfigureAwait(false);
        }
        catch (OracleException e) when (e.Number == Cancelado)
        {
            throw new BancoExpirouException(e);
        }
        catch (OracleException e) when (EhIndisponivel(e.Number))
        {
            throw new BancoIndisponivelException(e);
        }
    }

    public static bool EhIndisponivel(int numero) => Indisponivel.Contains(numero) || numero is >= 12500 and <= 12699;
}
