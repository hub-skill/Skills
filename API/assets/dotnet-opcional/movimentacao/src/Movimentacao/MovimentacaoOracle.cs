using Oracle.ManagedDataAccess.Client;

namespace ProjetoApi.Movimentacao;

/// <summary>
/// Movimentação no Oracle: o usuário da aplicação só tem INSERT em movimentacao e log_erros (pelos sinônimos criados no script
/// SQL). Cada lote é uma transação: a movimentação e, quando houver, o seu erro. A string de conexão vem de
/// <c>ConexoesOracle.Montar</c> (módulo bancos).
/// </summary>
public sealed class MovimentacaoOracle(string conexaoOracle, int timeoutSegundos = 5) : IRepositorioDeMovimentacao
{
    private const string SqlMovimentacao =
        "insert into movimentacao (requisicao_id, metodo, rota, requisicao_conteudo, requisicao_em, situacao_requisicao,"
        + " status_http, resposta_conteudo, resposta_em, situacao_resposta) values (:requisicao_id, :metodo, :rota,"
        + " :requisicao_conteudo, :requisicao_em, :situacao_requisicao, :status_http, :resposta_conteudo, :resposta_em,"
        + " :situacao_resposta)";

    private const string SqlErro =
        "insert into log_erros (requisicao_id, request_id, metodo, rota, status, codigo, titulo, tipo_excecao, duracao_ms,"
        + " ambiente) values (:requisicao_id, :request_id, :metodo, :rota, :status, :codigo, :titulo, :tipo_excecao,"
        + " :duracao_ms, :ambiente)";

    public async Task GravarAsync(IReadOnlyList<RegistroDeMovimentacao> registros, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registros);
        if (registros.Count == 0)
        {
            return;
        }

        using var conexao = new OracleConnection(conexaoOracle);
        await conexao.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transacao = conexao.BeginTransaction();

        // Um comando por tabela, reaproveitado para o lote inteiro (os CLOB não vão em array bind).
        using var movimentacao = Comando(conexao, transacao, SqlMovimentacao);
        var p = movimentacao.Parameters;
        var id = p.Add("requisicao_id", OracleDbType.Varchar2);
        var metodo = p.Add("metodo", OracleDbType.Varchar2);
        var rota = p.Add("rota", OracleDbType.Varchar2);
        var conteudoRequisicao = p.Add("requisicao_conteudo", OracleDbType.Clob);
        var requisicaoEm = p.Add("requisicao_em", OracleDbType.TimeStampTZ);
        var situacaoRequisicao = p.Add("situacao_requisicao", OracleDbType.Varchar2);
        var status = p.Add("status_http", OracleDbType.Int32);
        var conteudoResposta = p.Add("resposta_conteudo", OracleDbType.Clob);
        var respostaEm = p.Add("resposta_em", OracleDbType.TimeStampTZ);
        var situacaoResposta = p.Add("situacao_resposta", OracleDbType.Varchar2);

        using var erros = Comando(conexao, transacao, SqlErro);
        var e = erros.Parameters;
        var eId = e.Add("requisicao_id", OracleDbType.Varchar2);
        var eRequestId = e.Add("request_id", OracleDbType.Varchar2);
        var eMetodo = e.Add("metodo", OracleDbType.Varchar2);
        var eRota = e.Add("rota", OracleDbType.Varchar2);
        var eStatus = e.Add("status", OracleDbType.Int32);
        var eCodigo = e.Add("codigo", OracleDbType.Varchar2);
        var eTitulo = e.Add("titulo", OracleDbType.Varchar2);
        var eTipo = e.Add("tipo_excecao", OracleDbType.Varchar2);
        var eDuracao = e.Add("duracao_ms", OracleDbType.Int32);
        var eAmbiente = e.Add("ambiente", OracleDbType.Varchar2);

        foreach (var r in registros)
        {
            id.Value = r.RequisicaoId;
            metodo.Value = r.Metodo;
            rota.Value = r.Rota;
            conteudoRequisicao.Value = (object?)r.RequisicaoConteudo ?? DBNull.Value;
            requisicaoEm.Value = r.RequisicaoEm;
            situacaoRequisicao.Value = r.SituacaoRequisicao;
            status.Value = r.StatusHttp;
            conteudoResposta.Value = (object?)r.RespostaConteudo ?? DBNull.Value;
            respostaEm.Value = r.RespostaEm;
            situacaoResposta.Value = r.SituacaoResposta;
            await movimentacao.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (r.Erro is { } erro)
            {
                eId.Value = r.RequisicaoId;
                eRequestId.Value = erro.RequestId;
                eMetodo.Value = erro.Metodo;
                eRota.Value = erro.Rota;
                eStatus.Value = erro.Status;
                eCodigo.Value = erro.Codigo;
                eTitulo.Value = erro.Titulo;
                eTipo.Value = (object?)erro.TipoExcecao ?? DBNull.Value;
                eDuracao.Value = erro.DuracaoMs is int d ? d : DBNull.Value;
                eAmbiente.Value = erro.Ambiente;
                await erros.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transacao.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private OracleCommand Comando(OracleConnection conexao, OracleTransaction transacao, string sql)
    {
        var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.BindByName = true;
        comando.CommandTimeout = timeoutSegundos;
        comando.CommandText = sql;
        return comando;
    }
}
