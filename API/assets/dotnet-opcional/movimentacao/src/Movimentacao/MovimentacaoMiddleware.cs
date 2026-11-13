using System.Diagnostics;
using ProjetoApi.Configuration;
using ProjetoApi.Security;

namespace ProjetoApi.Movimentacao;

/// <summary>Limite do corpo lido para a movimentação (o mesmo limite de corpo da API; os testes usam o do Program.cs).</summary>
/// <param name="TamanhoMaximoDoCorpo">Corpo acima disso não é lido: só o tamanho é gravado.</param>
public sealed record MovimentacaoOpcoes(long TamanhoMaximoDoCorpo);

/// <summary>
/// Movimentação: toda requisição com chave de API válida (menos /health e /metrics) ganha um ULID na chegada (sem banco) e é
/// gravada, com a resposta, em segundo plano. É o primeiro middleware: enxerga a resposta final de tudo o que vem depois
/// (erros, autenticação, limite de requisições), que é guardada em memória até o fim para ser gravada e devolvida ao cliente com
/// o header X-Requisicao. O banco nunca está no caminho da resposta: com ele fora ou lento, a fila enche e as gravações a mais
/// se perdem (a API avisa no log). Mesmo comportamento do template Python (app/movimentacao/http.py).
/// </summary>
public sealed class MovimentacaoMiddleware(
    RequestDelegate proximo,
    FilaDeMovimentacao fila,
    MovimentacaoOpcoes opcoes,
    ApiSettings settings)
{
    public const string Header = "X-Requisicao";

    public async Task InvokeAsync(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        contexto.Items[RegistroDeErrosHttp.ChaveDoInicio] = Stopwatch.GetTimestamp(); // duração no log de erros

        var rota = contexto.Request.Path.Value is { Length: > 0 } caminho ? caminho : "/";

        // Só quem tem chave válida entra na movimentação: varreduras e chamadas sem chave recebem 401 e não gravam nada.
        var chaveDeApi = contexto.Request.Headers[ApiKeyAuthenticationHandler.Header].ToString();
        if (!RegrasDeMovimentacao.Movimentar(rota) || !ApiKeyAuthenticationHandler.ChaveValida(chaveDeApi, settings.ApiKey))
        {
            await proximo(contexto);
            return;
        }

        var chegada = DateTimeOffset.UtcNow;
        var id = Ulid.Novo(chegada);
        contexto.Items[RegistroDeErrosHttp.ChaveDaMovimentacao] = id;
        var conteudo = await LerConteudoAsync(contexto);

        // A resposta é guardada em memória (são JSONs pequenos) para gravar o que o cliente recebeu.
        var original = contexto.Response.Body;
        using var guardada = new MemoryStream();
        contexto.Response.Body = guardada;
        try
        {
            await proximo(contexto);
            await ConcluirAsync(contexto, id, rota, conteudo, chegada, guardada, original);
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

    private async Task ConcluirAsync(
        HttpContext contexto, string id, string rota, string? conteudo, DateTimeOffset chegada, MemoryStream guardada, Stream original)
    {
        var status = contexto.Response.StatusCode;
        var (situacaoRequisicao, situacaoResposta) = RegrasDeMovimentacao.Situacoes(status);
        var erro = contexto.Items.TryGetValue(RegistroDeErrosHttp.ChaveDoErro, out var e) ? e as RegistroDeErro : null;
        fila.Registrar(new RegistroDeMovimentacao(
            id,
            contexto.Request.Method.Length <= 10 ? contexto.Request.Method : contexto.Request.Method[..10],
            rota.Length <= 200 ? rota : rota[..200],
            conteudo,
            chegada,
            status,
            RegrasDeMovimentacao.ConteudoParaGravar(guardada.GetBuffer().AsSpan(0, (int)guardada.Length)),
            DateTimeOffset.UtcNow,
            situacaoRequisicao,
            situacaoResposta,
            erro));

        contexto.Response.Headers[Header] = id;
        guardada.Position = 0;
        await guardada.CopyToAsync(original, contexto.RequestAborted);
    }
}
