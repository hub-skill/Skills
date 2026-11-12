using System.Data;
using System.Globalization;
using Oracle.ManagedDataAccess.Client;

namespace ProjetoApi.Movimentacao;

/// <summary>
/// Movimentação no Oracle: o usuário da aplicação não tem acesso às tabelas, só executa as duas procedures (pelos
/// sinônimos criados no script SQL), que numeram, gravam e completam a linha. A string de conexão vem de
/// <c>ConexoesOracle.Montar</c> (módulo bancos).
/// </summary>
public sealed class MovimentacaoOracle(string conexaoOracle, int timeoutSegundos = 5) : IRepositorioDeMovimentacao
{
    public async Task<(int Ano, long Requisicao)> RegistrarRequisicaoAsync(
        string metodo, string rota, string? conteudo, CancellationToken cancellationToken)
    {
        using var conexao = new OracleConnection(conexaoOracle);
        await conexao.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transacao = conexao.BeginTransaction();
        using var comando = Procedure(conexao, transacao, "registrar_requisicao");
        comando.Parameters.Add("p_metodo", OracleDbType.Varchar2).Value = metodo;
        comando.Parameters.Add("p_rota", OracleDbType.Varchar2).Value = rota;
        comando.Parameters.Add("p_conteudo", OracleDbType.Clob).Value = (object?)conteudo ?? DBNull.Value;
        var ano = comando.Parameters.Add("p_ano", OracleDbType.Int32, ParameterDirection.Output);
        var requisicao = comando.Parameters.Add("p_requisicao", OracleDbType.Int64, ParameterDirection.Output);

        await comando.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transacao.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (
            Convert.ToInt32(ano.Value.ToString(), CultureInfo.InvariantCulture),
            Convert.ToInt64(requisicao.Value.ToString(), CultureInfo.InvariantCulture));
    }

    public async Task RegistrarRespostasAsync(
        IReadOnlyList<RegistroDeResposta> respostas, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(respostas);
        if (respostas.Count == 0)
        {
            return;
        }

        using var conexao = new OracleConnection(conexaoOracle);
        await conexao.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transacao = conexao.BeginTransaction();
        using var comando = Procedure(conexao, transacao, "registrar_resposta");
        var ano = comando.Parameters.Add("p_ano", OracleDbType.Int32);
        var requisicao = comando.Parameters.Add("p_requisicao", OracleDbType.Int64);
        var status = comando.Parameters.Add("p_status_http", OracleDbType.Int32);
        var conteudo = comando.Parameters.Add("p_conteudo", OracleDbType.Clob);
        var situacaoRequisicao = comando.Parameters.Add("p_situacao_requisicao", OracleDbType.Varchar2);
        var situacaoResposta = comando.Parameters.Add("p_situacao_resposta", OracleDbType.Varchar2);

        foreach (var r in respostas)
        {
            ano.Value = r.Ano;
            requisicao.Value = r.Requisicao;
            status.Value = r.StatusHttp;
            conteudo.Value = (object?)r.Conteudo ?? DBNull.Value;
            situacaoRequisicao.Value = r.SituacaoRequisicao;
            situacaoResposta.Value = r.SituacaoResposta;
            await comando.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transacao.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private OracleCommand Procedure(OracleConnection conexao, OracleTransaction transacao, string nome)
    {
        var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.BindByName = true;
        comando.CommandType = CommandType.StoredProcedure;
        comando.CommandText = nome;
        comando.CommandTimeout = timeoutSegundos;
        return comando;
    }
}

/// <summary>Grava o log de erros pelo sinônimo log_erros (o usuário da aplicação só tem INSERT na tabela do dono).</summary>
public sealed class LogDeErrosOracle(string conexaoOracle, int timeoutSegundos = 5) : IRepositorioDeErros
{
    private const string SqlInsert =
        "insert into log_erros (ano, requisicao, request_id, metodo, rota, status, codigo, titulo, tipo_excecao,"
        + " duracao_ms, ambiente) values (:ano, :requisicao, :request_id, :metodo, :rota, :status, :codigo, :titulo,"
        + " :tipo_excecao, :duracao_ms, :ambiente)";

    public async Task GravarAsync(IReadOnlyList<RegistroDeErro> registros, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registros);
        if (registros.Count == 0)
        {
            return;
        }

        using var conexao = new OracleConnection(conexaoOracle);
        await conexao.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transacao = conexao.BeginTransaction();
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.BindByName = true;
        comando.CommandTimeout = timeoutSegundos;
        comando.CommandText = SqlInsert;
        comando.ArrayBindCount = registros.Count; // um único envio para o lote inteiro

        Adicionar(comando, "ano", OracleDbType.Int32, registros.Select(r => (object)r.Ano));
        Adicionar(comando, "requisicao", OracleDbType.Int64, registros.Select(r => (object)r.Requisicao));
        Adicionar(comando, "request_id", OracleDbType.Varchar2, registros.Select(r => (object)r.RequestId));
        Adicionar(comando, "metodo", OracleDbType.Varchar2, registros.Select(r => (object)r.Metodo));
        Adicionar(comando, "rota", OracleDbType.Varchar2, registros.Select(r => (object)r.Rota));
        Adicionar(comando, "status", OracleDbType.Int32, registros.Select(r => (object)r.Status));
        Adicionar(comando, "codigo", OracleDbType.Varchar2, registros.Select(r => (object)r.Codigo));
        Adicionar(comando, "titulo", OracleDbType.Varchar2, registros.Select(r => (object)r.Titulo));
        Adicionar(comando, "tipo_excecao", OracleDbType.Varchar2, registros.Select(r => (object?)r.TipoExcecao ?? DBNull.Value));
        Adicionar(comando, "duracao_ms", OracleDbType.Int32, registros.Select(r => r.DuracaoMs is int d ? (object)d : DBNull.Value));
        Adicionar(comando, "ambiente", OracleDbType.Varchar2, registros.Select(r => (object)r.Ambiente));

        await comando.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transacao.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Adicionar(OracleCommand comando, string nome, OracleDbType tipo, IEnumerable<object> valores) =>
        comando.Parameters.Add(nome, tipo).Value = valores.ToArray();
}
