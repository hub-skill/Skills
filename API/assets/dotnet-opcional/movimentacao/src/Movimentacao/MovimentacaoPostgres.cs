using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;

namespace ProjetoApi.Movimentacao;

/// <summary>
/// Movimentação no PostgreSQL: o usuário da aplicação não tem acesso às tabelas, só executa as duas funções
/// (SECURITY DEFINER), que numeram, gravam e completam a linha. A sessão da API é somente leitura
/// (default_transaction_read_only): só as transações daqui pedem escrita.
/// </summary>
public sealed partial class MovimentacaoPostgres : IRepositorioDeMovimentacao
{
    private readonly NpgsqlDataSource _fonte;
    private readonly string _sqlRequisicao;
    private readonly string _sqlResposta;

    public MovimentacaoPostgres(NpgsqlDataSource fonte, string esquema)
    {
        ArgumentNullException.ThrowIfNull(fonte);
        ValidarEsquema(esquema);
        _fonte = fonte;
        _sqlRequisicao = $"select ano, requisicao from {esquema}.registrar_requisicao($1::varchar, $2::varchar, $3::text)";
        _sqlResposta =
            $"select {esquema}.registrar_resposta($1::smallint, $2::bigint, $3::smallint, $4::text, $5::char(2), $6::char(2))";
    }

    /// <summary>O nome do schema vem da configuração e entra no SQL: só identificador simples.</summary>
    internal static void ValidarEsquema(string esquema)
    {
        ArgumentNullException.ThrowIfNull(esquema);
        if (!IdentificadorSimples().IsMatch(esquema))
        {
            throw new ArgumentException("Esquema inválido.", nameof(esquema));
        }
    }

    public async Task<(int Ano, long Requisicao)> RegistrarRequisicaoAsync(
        string metodo, string rota, string? conteudo, CancellationToken cancellationToken)
    {
        await using var conexao = await _fonte.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transacao = await conexao.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await SomenteEstaTransacaoEscreveAsync(conexao, transacao, cancellationToken).ConfigureAwait(false);

#pragma warning disable CA2100 // o schema é validado (identificador simples); o resto do SQL é fixo
        await using var comando = new NpgsqlCommand(_sqlRequisicao, conexao, transacao);
#pragma warning restore CA2100
        comando.Parameters.Add(new NpgsqlParameter { Value = metodo });
        comando.Parameters.Add(new NpgsqlParameter { Value = rota });
        comando.Parameters.Add(new NpgsqlParameter<string?> { TypedValue = conteudo, NpgsqlDbType = NpgsqlDbType.Text });

        (int Ano, long Requisicao) numero;
        await using (var leitor = await comando.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await leitor.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("registrar_requisicao não devolveu o número.");
            }

            numero = (leitor.GetInt16(0), leitor.GetInt64(1));
        }

        await transacao.CommitAsync(cancellationToken).ConfigureAwait(false);
        return numero;
    }

    public async Task RegistrarRespostasAsync(
        IReadOnlyList<RegistroDeResposta> respostas, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(respostas);
        await using var conexao = await _fonte.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transacao = await conexao.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await SomenteEstaTransacaoEscreveAsync(conexao, transacao, cancellationToken).ConfigureAwait(false);

        await using var lote = new NpgsqlBatch(conexao, transacao);
        foreach (var r in respostas)
        {
            var comando = new NpgsqlBatchCommand(_sqlResposta);
            comando.Parameters.Add(new NpgsqlParameter { Value = (short)r.Ano });
            comando.Parameters.Add(new NpgsqlParameter { Value = r.Requisicao });
            comando.Parameters.Add(new NpgsqlParameter { Value = (short)r.StatusHttp });
            comando.Parameters.Add(new NpgsqlParameter<string?> { TypedValue = r.Conteudo, NpgsqlDbType = NpgsqlDbType.Text });
            comando.Parameters.Add(new NpgsqlParameter { Value = r.SituacaoRequisicao });
            comando.Parameters.Add(new NpgsqlParameter { Value = r.SituacaoResposta });
            lote.BatchCommands.Add(comando);
        }

        await lote.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transacao.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task SomenteEstaTransacaoEscreveAsync(
        NpgsqlConnection conexao, NpgsqlTransaction transacao, CancellationToken cancellationToken)
    {
        await using var leituraEscrita = new NpgsqlCommand("set transaction read write", conexao, transacao);
        await leituraEscrita.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex IdentificadorSimples();
}

/// <summary>Grava o log de erros em &lt;schema&gt;.log_erros (o usuário da aplicação só tem INSERT nela).</summary>
public sealed class LogDeErrosPostgres : IRepositorioDeErros
{
    private readonly NpgsqlDataSource _fonte;
    private readonly string _sqlInsert;

    public LogDeErrosPostgres(NpgsqlDataSource fonte, string esquema)
    {
        ArgumentNullException.ThrowIfNull(fonte);
        MovimentacaoPostgres.ValidarEsquema(esquema);
        _fonte = fonte;
        _sqlInsert = $"insert into {esquema}.log_erros"
            + " (ano, requisicao, request_id, metodo, rota, status, codigo, titulo, tipo_excecao, duracao_ms, ambiente)"
            + " values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)";
    }

    public async Task GravarAsync(IReadOnlyList<RegistroDeErro> registros, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registros);
        await using var conexao = await _fonte.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transacao = await conexao.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await MovimentacaoPostgres.SomenteEstaTransacaoEscreveAsync(conexao, transacao, cancellationToken).ConfigureAwait(false);

        await using var lote = new NpgsqlBatch(conexao, transacao);
        foreach (var r in registros)
        {
            var comando = new NpgsqlBatchCommand(_sqlInsert);
            comando.Parameters.Add(new NpgsqlParameter { Value = (short)r.Ano });
            comando.Parameters.Add(new NpgsqlParameter { Value = r.Requisicao });
            comando.Parameters.Add(new NpgsqlParameter { Value = r.RequestId });
            comando.Parameters.Add(new NpgsqlParameter { Value = r.Metodo });
            comando.Parameters.Add(new NpgsqlParameter { Value = r.Rota });
            comando.Parameters.Add(new NpgsqlParameter { Value = (short)r.Status });
            comando.Parameters.Add(new NpgsqlParameter { Value = r.Codigo });
            comando.Parameters.Add(new NpgsqlParameter { Value = r.Titulo });
            comando.Parameters.Add(new NpgsqlParameter<string?> { TypedValue = r.TipoExcecao, NpgsqlDbType = NpgsqlDbType.Varchar });
            comando.Parameters.Add(new NpgsqlParameter<int?> { TypedValue = r.DuracaoMs, NpgsqlDbType = NpgsqlDbType.Integer });
            comando.Parameters.Add(new NpgsqlParameter { Value = r.Ambiente });
            lote.BatchCommands.Add(comando);
        }

        await lote.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transacao.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
