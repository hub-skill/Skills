using Npgsql;

namespace ProjetoApi.Bancos;

public static class ConexoesPostgres
{
    /// <summary>
    /// String de conexão segura. Com somenteLeitura, a sessão abre como read only e cada comando tem
    /// statement_timeout (estourou = BancoExpirouException). Senha vem do .env/variável de ambiente.
    /// </summary>
    public static string Montar(
        string host,
        int porta,
        string banco,
        string usuario,
        string senha,
        bool somenteLeitura,
        int timeoutSegundos = 5,
        string aplicacao = "projeto-api",
        SslMode sslMode = SslMode.Prefer)
    {
        var construtor = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = porta,
            Database = banco,
            Username = usuario,
            Password = senha,
            SslMode = sslMode, // remoto ou nuvem: Require ou VerifyFull
            // Sem Kerberos: o padrão (Prefer) tenta carregar a libgssapi, que não existe na imagem chiseled
            // (aviso "Cannot load library libgssapi_krb5.so.2") e gasta uma ida e volta a mais por conexão.
            GssEncryptionMode = GssEncryptionMode.Disable,
            Timeout = 5,
            ApplicationName = aplicacao,
            MaxPoolSize = 5,
        };
        if (somenteLeitura)
        {
            construtor.CommandTimeout = timeoutSegundos + 2;
            construtor.Options = $"-c default_transaction_read_only=on -c statement_timeout={timeoutSegundos * 1000}";
        }

        return construtor.ConnectionString;
    }
}

/// <summary>Traduz as exceções do Npgsql para BancoIndisponivel (503) e BancoExpirou (504).</summary>
public static class ErrosPostgres
{
    public static async Task<T> TraduzirAsync<T>(Func<Task<T>> operacao)
    {
        ArgumentNullException.ThrowIfNull(operacao);
        try
        {
            return await operacao().ConfigureAwait(false);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.QueryCanceled)
        {
            throw new BancoExpirouException(e); // statement_timeout
        }
        catch (PostgresException e) when (ErroDeConexao(e.SqlState))
        {
            throw new BancoIndisponivelException(e);
        }
        catch (NpgsqlException e) when (e is not PostgresException)
        {
            // Rede, servidor fora do ar ou tempo limite do driver. Nunca registre a exceção inteira em log.
            if (e.InnerException is TimeoutException)
            {
                throw new BancoExpirouException(e);
            }

            throw new BancoIndisponivelException(e);
        }
    }

    // Classes 08 (conexão), 28 (autenticação), 53 (recursos) e 57P (servidor desligando).
    public static bool ErroDeConexao(string sqlState)
    {
        ArgumentNullException.ThrowIfNull(sqlState);
        return sqlState.StartsWith("08", StringComparison.Ordinal)
            || sqlState.StartsWith("28", StringComparison.Ordinal)
            || sqlState.StartsWith("53", StringComparison.Ordinal)
            || sqlState.StartsWith("57P", StringComparison.Ordinal);
    }
}
