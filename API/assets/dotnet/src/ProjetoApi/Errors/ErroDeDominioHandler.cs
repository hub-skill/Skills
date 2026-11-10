using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ProjetoApi.Errors;

public sealed class ErroDeDominioHandler(ILogger<ErroDeDominioHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, codigo, mensagem) = exception switch
        {
            ErroDeDominio erro => (erro.StatusCode, erro.Codigo, erro.Mensagem),
            _ => (StatusCodes.Status500InternalServerError, "erro_interno",
                  "Erro interno. Informe o request_id ao suporte.")
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            // Stack trace apenas nos logs internos; o cliente recebe mensagem genérica.
            logger.LogError(exception, "erro não tratado");
        }

        var problema = new ProblemDetails { Status = status, Title = mensagem };
        problema.Extensions["code"] = codigo;
        problema.Extensions["request_id"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problema, cancellationToken);
        return true;
    }
}
