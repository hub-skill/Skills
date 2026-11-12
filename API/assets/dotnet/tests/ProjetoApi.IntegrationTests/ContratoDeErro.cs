using System.Net;
using System.Text.Json;
using Xunit;

namespace ProjetoApi.IntegrationTests;

/// <summary>
/// Contrato de erro da API: RFC 9457 (Problem Details).
/// É o mesmo contrato do template Python (tests/contrato.py); os dois conjuntos de testes conferem as mesmas coisas.
/// </summary>
internal static class ContratoDeErro
{
    private static readonly string[] CamposDoProblema = ["code", "request_id", "status", "title"];

    /// <summary>Confere status, tipo de conteúdo, conjunto exato de campos, <c>status</c> e <c>code</c> do corpo.</summary>
    public static async Task<JsonElement> AfirmarProblema(HttpResponseMessage resposta, HttpStatusCode status, string codigo)
    {
        Assert.Equal(status, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);

        using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        var corpo = documento.RootElement.Clone();

        Assert.Equal(CamposDoProblema, corpo.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal((int)status, corpo.GetProperty("status").GetInt32());
        Assert.Equal(codigo, corpo.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(corpo.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrEmpty(corpo.GetProperty("request_id").GetString()));
        return corpo;
    }
}
