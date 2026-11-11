using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

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
            // JSON malformado ou corpo ilegível: 400, nunca 500.
            BadHttpRequestException erro => (erro.StatusCode, "requisicao_invalida", "Requisição inválida."),
            _ => (StatusCodes.Status500InternalServerError, "erro_interno",
                  "Erro interno. Informe o request_id ao suporte.")
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            // Stack trace apenas nos logs internos; o cliente recebe mensagem genérica.
            LogErroNaoTratado(logger, exception);
        }

        var problema = new ProblemDetails { Status = status, Title = mensagem };
        problema.Extensions["code"] = codigo;
        problema.Extensions["request_id"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problema, cancellationToken);
        return true;
    }
}
