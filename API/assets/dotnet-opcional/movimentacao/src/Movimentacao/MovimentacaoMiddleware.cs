using System.Diagnostics;
using ProjetoApi.Configuration;
using ProjetoApi.Security;

namespace ProjetoApi.Movimentacao;

/// <summary>Limites e prazos da movimentação (valores de produção em Program.cs; os testes trocam o prazo).</summary>
/// <param name="PrazoParaNumerar">Quanto a chegada espera o banco devolver o número; passou, a requisição segue sem.</param>
/// <param name="TamanhoMaximoDoCorpo">Corpo acima disso não é lido: só o tamanho é gravado.</param>
public sealed record MovimentacaoOpcoes(TimeSpan PrazoParaNumerar, long TamanhoMaximoDoCorpo);

/// <summary>
/// Movimentação: toda requisição com chave de API válida (menos /health e /metrics) é numerada e gravada na
/// chegada, e a resposta completa a mesma linha em segundo plano. É o primeiro middleware: enxerga a resposta
/// final de tudo o que vem depois (erros, autenticação, limite de requisições), que é guardada em memória até o
/// fim para ser gravada e devolvida ao cliente com o header X-Requisicao. Mesmo comportamento do template
/// Python (app/movimentacao/http.py).
/// </summary>
public sealed partial class MovimentacaoMiddleware(
    RequestDelegate proximo,
    IRepositorioDeMovimentacao repositorio,
    FilaDeRespostas respostas,
    MovimentacaoOpcoes opcoes,
    ApiSettings settings,
    ILogger<MovimentacaoMiddleware> logger)
{
    public const string Header = "X-Requisicao";

    public async Task InvokeAsync(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        contexto.Items[RegistroDeErrosHttp.ChaveDoInicio] = Stopwatch.GetTimestamp(); // duração no log de erros

        var rota = contexto.Request.Path.Value is { Length: > 0 } caminho ? caminho : "/";

        // Só quem tem chave válida entra na movimentação: varreduras e chamadas sem chave recebem 401 e não gravam
        // nada no banco (nem a movimentação, nem o log de erros).
        var chaveDeApi = contexto.Request.Headers[ApiKeyAuthenticationHandler.Header].ToString();
        if (!RegrasDeMovimentacao.Movimentar(rota) || !ApiKeyAuthenticationHandler.ChaveValida(chaveDeApi, settings.ApiKey))
        {
            await proximo(contexto);
            return;
        }

        var conteudo = await LerConteudoAsync(contexto);
        await NumerarAsync(contexto, rota, conteudo);
        if (!contexto.Items.TryGetValue(RegistroDeErrosHttp.ChaveDaMovimentacao, out var numero) || numero is not ValueTuple<int, long> chave)
        {
            await proximo(contexto); // sem número (banco fora ou lento): segue sem movimentação
            return;
        }

        // A resposta é guardada em memória (são JSONs pequenos) para gravar o que o cliente recebeu.
        var original = contexto.Response.Body;
        using var guardada = new MemoryStream();
        contexto.Response.Body = guardada;
        try
        {
            await proximo(contexto);
            await ConcluirAsync(contexto, chave, guardada, original);
        }
        finally
        {
            contexto.Response.Body = original;
        }
    }

    private async Task<string?> LerConteudoAsync(HttpContext contexto)
    {
        var requisicao = contexto.Request;
        if (requisicao.ContentLength > opcoes.TamanhoMaximoDoCorpo)
        {
            // Não lê o que vai ser recusado com 413 (o corpo acima do limite não é gravado).
            return $"(corpo de {requisicao.ContentLength} bytes, acima do limite: não gravado)";
        }

        requisicao.EnableBuffering();
        try
        {
            using var memoria = new MemoryStream();
            await requisicao.Body.CopyToAsync(memoria, contexto.RequestAborted);
            requisicao.Body.Position = 0; // o resto do pipeline lê o corpo de novo
            return RegrasDeMovimentacao.ConteudoParaGravar(memoria.GetBuffer().AsSpan(0, (int)memoria.Length));
        }
        catch (BadHttpRequestException)
        {
            // Corpo sem Content-Length que passou do limite do Kestrel: o erro sai do pipeline, como antes.
            return "(corpo acima do limite: não gravado)";
        }
    }

    private async Task NumerarAsync(HttpContext contexto, string rota, string? conteudo)
    {
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(contexto.RequestAborted);
        prazo.CancelAfter(opcoes.PrazoParaNumerar);
        try
        {
            contexto.Items[RegistroDeErrosHttp.ChaveDaMovimentacao] =
                await repositorio.RegistrarRequisicaoAsync(contexto.Request.Method, rota, conteudo, prazo.Token);
        }
#pragma warning disable CA1031 // sem número a API continua respondendo: qualquer falha do banco só é registrada
        catch (Exception e) when (!contexto.RequestAborted.IsCancellationRequested)
#pragma warning restore CA1031
        {
            LogSemNumero(logger, e.GetType().Name);
        }
    }

    private async Task ConcluirAsync(
        HttpContext contexto, (int Ano, long Requisicao) chave, MemoryStream guardada, Stream original)
    {
        var status = contexto.Response.StatusCode;
        var (situacaoRequisicao, situacaoResposta) = RegrasDeMovimentacao.Situacoes(status);
        respostas.Registrar(new RegistroDeResposta(
            chave.Ano,
            chave.Requisicao,
            status,
            RegrasDeMovimentacao.ConteudoParaGravar(guardada.GetBuffer().AsSpan(0, (int)guardada.Length)),
            situacaoRequisicao,
            situacaoResposta));

        contexto.Response.Headers[Header] = RegrasDeMovimentacao.NumeroFormatado(chave.Ano, chave.Requisicao);
        guardada.Position = 0;
        await guardada.CopyToAsync(original, contexto.RequestAborted);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "requisição sem número na movimentação: {Tipo}")]
    private static partial void LogSemNumero(ILogger logger, string tipo);
}
