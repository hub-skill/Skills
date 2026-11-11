using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using ProjetoApi.Bancos;
using Xunit;

namespace ProjetoApi.UnitTests;

/// <summary>
/// Drivers reais (Npgsql e ODP.NET) apontando para uma porta sem servidor: a falha tem de virar
/// BancoIndisponivelException (503 na API), e não escapar como exceção do driver (500).
/// Não precisa de banco no ar: roda também no CI.
/// </summary>
public sealed class FalhasDeBancoTests
{
    private const int PortaSemServidor = 1;

    [Fact]
    public async Task Postgres_fora_do_ar_vira_banco_indisponivel()
    {
        var conexao = ConexoesPostgres.Montar("127.0.0.1", PortaSemServidor, "banco", "usuario", "x", somenteLeitura: true);
        await using var fonte = NpgsqlDataSource.Create(conexao);

        await Assert.ThrowsAsync<BancoIndisponivelException>(() => ErrosPostgres.TraduzirAsync(async () =>
        {
            await using var comando = fonte.CreateCommand("select 1");
            return await comando.ExecuteScalarAsync();
        }));
    }

    [Fact]
    public async Task Oracle_fora_do_ar_vira_banco_indisponivel()
    {
        var conexao = ConexoesOracle.Montar($"127.0.0.1:{PortaSemServidor}/XEPDB1", "usuario", "x");

        await Assert.ThrowsAsync<BancoIndisponivelException>(() => ErrosOracle.TraduzirAsync(async () =>
        {
            await using var con = new OracleConnection(conexao);
            await con.OpenAsync();
            return 1;
        }));
    }

    [Theory]
    [InlineData("08006", true)]   // conexão perdida
    [InlineData("28P01", true)]   // senha errada
    [InlineData("53300", true)]   // conexões esgotadas
    [InlineData("57P01", true)]   // servidor desligando
    [InlineData("42P01", false)]  // tabela inexistente: erro de programação, deve continuar 500
    [InlineData("23505", false)]  // unicidade: erro de negócio, trate no repositório
    public void Postgres_classifica_sqlstate(string sqlState, bool indisponivel) =>
        Assert.Equal(indisponivel, ErrosPostgres.ErroDeConexao(sqlState));

    [Theory]
    [InlineData(50201, true)]   // ODP.NET: falha de rede embrulhada
    [InlineData(12541, true)]   // sem listener
    [InlineData(1017, true)]    // senha errada
    [InlineData(12600, true)]   // faixa 125xx-126xx de rede
    [InlineData(942, false)]    // tabela inexistente: erro de programação
    public void Oracle_classifica_codigos(int numero, bool indisponivel) =>
        Assert.Equal(indisponivel, ErrosOracle.EhIndisponivel(numero));

    [Fact]
    public void String_de_conexao_somente_leitura_tem_timeout_e_read_only()
    {
        var construtor = new NpgsqlConnectionStringBuilder(
            ConexoesPostgres.Montar("h", 5432, "b", "u", "s", somenteLeitura: true, timeoutSegundos: 3));
        Assert.Contains("default_transaction_read_only=on", construtor.Options, StringComparison.Ordinal);
        Assert.Contains("statement_timeout=3000", construtor.Options, StringComparison.Ordinal);
        Assert.Equal(5, construtor.CommandTimeout);
    }

    [Fact]
    public async Task Health_check_fica_indisponivel_quando_o_banco_falha()
    {
        var saudavel = await new BancoHealthCheck(_ => Task.CompletedTask).CheckHealthAsync(new HealthCheckContext());
        var doente = await new BancoHealthCheck(_ => throw new BancoIndisponivelException()).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, saudavel.Status);
        Assert.Equal(HealthStatus.Unhealthy, doente.Status);
    }
}
