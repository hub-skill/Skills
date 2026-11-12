using System.Text.Json;

namespace ProjetoApi.Middleware;

/// <summary>
/// Corpo JSON aninhado fundo demais responde 400 requisicao_invalida, como no Python (o parser dele estoura a
/// recursão perto de 1000 níveis). Sem isto, o .NET para no primeiro tipo errado e responde 422 sem medir a
/// profundidade. Lê o corpo (já limitado a 64 KB) uma vez e volta ao início para o endpoint. Registre depois de
/// UseAuthorization: só quem tem chave faz a API ler o corpo.
/// </summary>
public sealed class ProfundidadeDoJsonMiddleware(RequestDelegate proximo)
{
    public const int ProfundidadeMaxima = 1000;

    public async Task InvokeAsync(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        if (contexto.Request.HasJsonContentType())
        {
            contexto.Request.EnableBuffering();
            try
            {
                using var _ = await JsonDocument.ParseAsync(
                    contexto.Request.Body,
                    new JsonDocumentOptions { MaxDepth = ProfundidadeMaxima },
                    contexto.RequestAborted);
            }
            catch (JsonException)
            {
                // Sintaxe errada ou profundidade acima do máximo: corpo ilegível.
                throw new BadHttpRequestException("Requisição inválida.", StatusCodes.Status400BadRequest);
            }
            finally
            {
                contexto.Request.Body.Position = 0;
            }
        }

        await proximo(contexto);
    }
}
