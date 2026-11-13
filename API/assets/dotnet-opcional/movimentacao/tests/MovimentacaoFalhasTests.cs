using Npgsql;
using Oracle.ManagedDataAccess.Client;
using ProjetoApi.Bancos;
using ProjetoApi.Movimentacao;
using Xunit;

namespace ProjetoApi.UnitTests;

/// <summary>
/// Movimentação com o banco fora do ar: drivers reais apontando para uma porta sem servidor. A falha chega ao gravador em segundo
/// plano (que só a registra no log) e nunca à requisição. Não precisa de banco: roda no CI.
/// </summary>
public sealed class MovimentacaoFalhasTests
{
    private const int PortaSemServidor = 1;

    private static readonly RegistroDeErro Erro =
        new("r1", "POST", "/x", 503, "banco_indisponivel", "Banco fora.", null, 1, "Teste");

    private static readonly RegistroDeMovimentacao Registro = new(
        "01JC4M7Z9Q8T2X5V6B3N0WYKHD", "POST", "/x", "{}", DateTimeOffset.UtcNow, 503, "{}", DateTimeOffset.UtcNow, "OK", "ER", Erro);

    [Fact]
    public async Task Postgres_fora_do_ar_falha_sem_travar()
    {
        await using var fonte = NpgsqlDataSource.Create(
            ConexoesPostgres.Montar("127.0.0.1", PortaSemServidor, "banco", "usuario", "x", somenteLeitura: true)); // senha falsa
        var movimentacao = new MovimentacaoPostgres(fonte, "app");

        await Assert.ThrowsAnyAsync<Exception>(() => movimentacao.GravarAsync([Registro], CancellationToken.None));
    }

    [Fact]
    public async Task Oracle_fora_do_ar_falha_sem_travar()
    {
        var movimentacao = new MovimentacaoOracle(ConexoesOracle.Montar($"127.0.0.1:{PortaSemServidor}/XEPDB1", "usuario", "x")); // senha falsa

        await Assert.ThrowsAsync<OracleException>(() => movimentacao.GravarAsync([Registro], CancellationToken.None));
    }

    [Fact]
    public async Task Lote_vazio_nao_toca_no_banco()
    {
        await using var fonte = NpgsqlDataSource.Create(
            ConexoesPostgres.Montar("127.0.0.1", PortaSemServidor, "banco", "usuario", "x", somenteLeitura: true));

        await new MovimentacaoPostgres(fonte, "app").GravarAsync([], CancellationToken.None); // não lança: nem conecta
        await new MovimentacaoOracle(ConexoesOracle.Montar($"127.0.0.1:{PortaSemServidor}/XEPDB1", "usuario", "x")).GravarAsync([], CancellationToken.None);
    }

    [Theory]
    [InlineData("app; drop table x")]
    [InlineData("app.outro")]
    [InlineData("")]
    public void Nome_de_esquema_invalido_e_recusado(string esquema)
    {
        // O nome do schema entra no SQL: só identificador simples.
        using var fonte = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=x;Username=u;Password=x");
        Assert.Throws<ArgumentException>(() => new MovimentacaoPostgres(fonte, esquema));
    }
}
