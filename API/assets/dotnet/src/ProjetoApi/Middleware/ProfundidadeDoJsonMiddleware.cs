using System.Text.Json;
using System.Text.Unicode;

namespace ProjetoApi.Middleware;

/// <summary>
/// Corpo JSON que o template Python recusa na leitura responde 400 requisicao_invalida aqui também:
/// mais de 64 níveis de aninhamento ou UTF-8 inválido. Sem isto, o .NET para no primeiro tipo errado (422), só
/// mede a profundidade ao pular campos desconhecidos e só percebe o UTF-8 inválido ao converter o campo (422).
/// Lê o corpo (já limitado pelo tamanho máximo) uma vez e volta ao início para o endpoint.
/// Registrado depois de UseAuthorization: sem chave a resposta é 401 e o corpo nem é lido.
/// </summary>
public sealed class ProfundidadeDoJsonMiddleware(RequestDelegate proximo)
{
    public const int ProfundidadeMaxima = 64;

    public async Task InvokeAsync(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        // Corpo vazio fica para o endpoint (422, como no template Python).
        if (contexto.Request.HasJsonContentType() && contexto.Request.ContentLength != 0)
        {
            contexto.Request.EnableBuffering();
            using var memoria = new MemoryStream();
            await contexto.Request.Body.CopyToAsync(memoria, contexto.RequestAborted);
            contexto.Request.Body.Position = 0;

            var corpo = memoria.GetBuffer().AsMemory(0, (int)memoria.Length);
            if (!Utf8.IsValid(corpo.Span) || !JsonLegivel(corpo))
            {
                throw new BadHttpRequestException("Requisição inválida.", StatusCodes.Status400BadRequest);
            }
        }

        await proximo(contexto);
    }

    private static bool JsonLegivel(ReadOnlyMemory<byte> corpo)
    {
        try
        {
            using var _ = JsonDocument.Parse(corpo, new JsonDocumentOptions { MaxDepth = ProfundidadeMaxima });
            return true;
        }
        catch (JsonException)
        {
            return false; // sintaxe errada ou profundidade acima do máximo
        }
    }
}
