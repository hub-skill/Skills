using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;

namespace ProjetoApi.Movimentacao;

/// <summary>
/// Movimentação no PostgreSQL: o usuário da aplicação só tem INSERT em movimentacao e log_erros (nada de leitura, alteração
/// ou exclusão). Cada lote é uma transação: a movimentação e, quando houver, o seu erro. A sessão da API é somente leitura
/// (default_transaction_read_only): só as transações daqui pedem escrita.
/// </summary>
public sealed partial class MovimentacaoPostgres : IRepositorioDeMovimentacao
{
    private readonly NpgsqlDataSource _fonte;
    private readonly string _sqlMovimentacao;
    private readonly string _sqlErro;

    public MovimentacaoPostgres(NpgsqlDataSource fonte, string esquema)
    {
        ArgumentNullException.ThrowIfNull(fonte);
        ValidarEsquema(esquema);
        _fonte = fonte;
        _sqlMovimentacao =
            $"insert into {esquema}.movimentacao (requisicao_id, metodo, rota, requisicao_conteudo, requisicao_em,"
            + " situacao_requisicao, status_http, resposta_conteudo, resposta_em, situacao_resposta)"
            + " values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)";
        _sqlErro =
            $"insert into {esquema}.log_erros (requisicao_id, request_id, metodo, rota, status, codigo, titulo, tipo_excecao,"
            + " duracao_ms, ambiente) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)";
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

    public async Task GravarAsync(IReadOnlyList<RegistroDeMovimentacao> registros, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registros);
        if (registros.Count == 0)
        {
            return;
        }

        await using var conexao = await _fonte.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transacao = await conexao.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var leituraEscrita = new NpgsqlCommand("set transaction read write", conexao, transacao))
        {
            await leituraEscrita.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var lote = new NpgsqlBatch(conexao, transacao);
        foreach (var r in registros)
        {
            var comando = new NpgsqlBatchCommand(_sqlMovimentacao);
            comando.Parameters.Add(new NpgsqlParameter { Value = r.RequisicaoId });
            comando.Parameters.Add(new NpgsqlParameter { Value = r.Metodo });
            comando.Parameters.Add(new NpgsqlParameter { Value = r.Rota });
            comando.Parameters.Add(new NpgsqlParameter<string?> { TypedValue = r.RequisicaoConteudo, NpgsqlDbType = NpgsqlDbType.Text });
            comando.Parameters.Add(new NpgsqlParameter { Value = r.RequisicaoEm.UtcDateTime, NpgsqlDbType = NpgsqlDbType.TimestampTz });
            comando.Parameters.Add(new NpgsqlParameter { Value = r.SituacaoRequisicao });
            comando.Parameters.Add(new NpgsqlParameter { Value = (short)r.StatusHttp });
            comando.Parameters.Add(new NpgsqlParameter<string?> { TypedValue = r.RespostaConteudo, NpgsqlDbType = NpgsqlDbType.Text });
            comando.Parameters.Add(new NpgsqlParameter { Value = r.RespostaEm.UtcDateTime, NpgsqlDbType = NpgsqlDbType.TimestampTz });
            comando.Parameters.Add(new NpgsqlParameter { Value = r.SituacaoResposta });
            lote.BatchCommands.Add(comando);

            if (r.Erro is { } e)
            {
                var erro = new NpgsqlBatchCommand(_sqlErro);
                erro.Parameters.Add(new NpgsqlParameter { Value = r.RequisicaoId });
                erro.Parameters.Add(new NpgsqlParameter { Value = e.RequestId });
                erro.Parameters.Add(new NpgsqlParameter { Value = e.Metodo });
                erro.Parameters.Add(new NpgsqlParameter { Value = e.Rota });
                erro.Parameters.Add(new NpgsqlParameter { Value = (short)e.Status });
                erro.Parameters.Add(new NpgsqlParameter { Value = e.Codigo });
                erro.Parameters.Add(new NpgsqlParameter { Value = e.Titulo });
                erro.Parameters.Add(new NpgsqlParameter<string?> { TypedValue = e.TipoExcecao, NpgsqlDbType = NpgsqlDbType.Varchar });
                erro.Parameters.Add(new NpgsqlParameter<int?> { TypedValue = e.DuracaoMs, NpgsqlDbType = NpgsqlDbType.Integer });
                erro.Parameters.Add(new NpgsqlParameter { Value = e.Ambiente });
                lote.BatchCommands.Add(erro);
            }
        }

        await lote.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transacao.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex IdentificadorSimples();
}
