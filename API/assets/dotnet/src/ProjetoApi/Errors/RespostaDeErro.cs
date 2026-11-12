using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace ProjetoApi.Errors;

/// <summary>
/// Formato único de erro: RFC 9457 (Problem Details), o mesmo do template Python.
/// O corpo tem sempre exatamente <c>title</c>, <c>status</c>, <c>code</c> e <c>request_id</c> (sem <c>type</c>:
/// ausente = about:blank) e o tipo de conteúdo é <c>application/problem+json</c>.
/// </summary>
public static class RespostaDeErro
{
    public const string TipoDeConteudo = "application/problem+json";

    /// <summary>Título em português dos erros genéricos do framework (401, 404, 405). Outros: mantém o do framework.</summary>
    public static string? TituloGenerico(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => "Chave de API ausente ou inválida.",
        StatusCodes.Status404NotFound => "Recurso não encontrado.",
        StatusCodes.Status405MethodNotAllowed => "Método não permitido.",
        _ => null
    };

    /// <summary>
    /// Erros que o framework produz sem corpo (401, 404, 405...): código <c>http_&lt;status&gt;</c> e título em
    /// português quando conhecido. Usado pelo <c>UseStatusCodePages</c>.
    /// </summary>
    public static Task EscreverGenericoAsync(HttpContext contexto)
    {
        var status = contexto.Response.StatusCode;
        var frase = ReasonPhrases.GetReasonPhrase(status);
        return EscreverAsync(
            contexto,
            status,
            string.Create(CultureInfo.InvariantCulture, $"http_{status}"),
            TituloGenerico(status) ?? (string.IsNullOrEmpty(frase) ? "Erro" : frase),
            contexto.RequestAborted);
    }

    public static Task EscreverAsync(
        HttpContext contexto, int status, string codigo, string titulo, CancellationToken cancellationToken)
    {
        var problema = new ProblemDetails { Status = status, Title = titulo };
        problema.Extensions["code"] = codigo;
        problema.Extensions["request_id"] = contexto.TraceIdentifier;

        contexto.Response.StatusCode = status;
        return contexto.Response.WriteAsJsonAsync(
            problema, options: null, contentType: TipoDeConteudo, cancellationToken: cancellationToken);
    }
}
