using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;

namespace ProjetoApi.Errors;

public sealed partial class ErroDeDominioHandler(ILogger<ErroDeDominioHandler> logger) : IExceptionHandler
{
    [LoggerMessage(Level = LogLevel.Error, Message = "erro não tratado")]
    private static partial void LogErroNaoTratado(ILogger logger, Exception exception);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, codigo, mensagem) = exception switch
        {
            ErroDeDominioException erro => (erro.StatusCode, erro.Codigo, erro.Mensagem),
            // Corpo com problema nunca é 500. JSON válido com forma errada (tipo errado, campo desconhecido, lista no
            // lugar de objeto): 422, como no Python. Corpo ilegível (sintaxe ou profundidade): 400.
            BadHttpRequestException { InnerException: JsonException json } when !CorpoIlegivel(json) =>
                (StatusCodes.Status422UnprocessableEntity, "entrada_invalida", "Dados de entrada inválidos."),
            BadHttpRequestException erro => (erro.StatusCode, "requisicao_invalida", "Requisição inválida."),
            _ => (StatusCodes.Status500InternalServerError, "erro_interno",
                  "Erro interno. Informe o request_id ao suporte.")
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            // Stack trace apenas nos logs internos; o cliente recebe mensagem genérica.
            LogErroNaoTratado(logger, exception);
        }

        await RespostaDeErro.EscreverAsync(httpContext, status, codigo, mensagem, cancellationToken);
        return true;
    }

    // O JsonReaderException do System.Text.Json é interno: identificado pelo nome. Os testes de integração
    // (JSON malformado = 400, tipo errado = 422) pegam se uma versão nova do .NET mudar isso.
    private static bool CorpoIlegivel(JsonException erro) =>
        erro.InnerException?.GetType().Name == "JsonReaderException";
}
