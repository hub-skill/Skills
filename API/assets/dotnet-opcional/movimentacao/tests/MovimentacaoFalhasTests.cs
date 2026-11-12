using Npgsql;
using Oracle.ManagedDataAccess.Client;
using ProjetoApi.Bancos;
using ProjetoApi.Movimentacao;
using Xunit;

namespace ProjetoApi.UnitTests;

/// <summary>
/// Movimentação com o banco fora do ar: drivers reais apontando para uma porta sem servidor. A falha chega ao
/// middleware como exceção (que o trata: segue sem movimentação) e não trava. Não precisa de banco: roda no CI.
/// </summary>
public sealed class MovimentacaoFalhasTests
{
    private const int PortaSemServidor = 1;

    private static readonly RegistroDeResposta Resposta = new(2026, 1, 503, "{}", "OK", "ER");

    private static readonly RegistroDeErro Erro =
        new(2026, 1, "r1", "POST", "/x", 503, "banco_indisponivel", "Banco fora.", null, 1, "Teste");

    [Fact]
    public async Task Postgres_fora_do_ar_falha_sem_travar()
    {
        await using var fonte = NpgsqlDataSource.Create(
            ConexoesPostgres.Montar("127.0.0.1", PortaSemServidor, "banco", "usuario", "x", somenteLeitura: true)); // senha falsa
        var movimentacao = new MovimentacaoPostgres(fonte, "app");
        var erros = new LogDeErrosPostgres(fonte, "app");

        await Assert.ThrowsAnyAsync<Exception>(() => movimentacao.RegistrarRequisicaoAsync("POST", "/x", "{}", CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() => movimentacao.RegistrarRespostasAsync([Resposta], CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() => erros.GravarAsync([Erro], CancellationToken.None));
    }

    [Fact]
    public async Task Oracle_fora_do_ar_falha_sem_travar()
    {
        var conexao = ConexoesOracle.Montar($"127.0.0.1:{PortaSemServidor}/XEPDB1", "usuario", "x"); // senha falsa
        var movimentacao = new MovimentacaoOracle(conexao);
        var erros = new LogDeErrosOracle(conexao);

        await Assert.ThrowsAsync<OracleException>(() => movimentacao.RegistrarRequisicaoAsync("POST", "/x", "{}", CancellationToken.None));
        await Assert.ThrowsAsync<OracleException>(() => movimentacao.RegistrarRespostasAsync([Resposta], CancellationToken.None));
        await Assert.ThrowsAsync<OracleException>(() => erros.GravarAsync([Erro], CancellationToken.None));
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
        Assert.Throws<ArgumentException>(() => new LogDeErrosPostgres(fonte, esquema));
    }
}
