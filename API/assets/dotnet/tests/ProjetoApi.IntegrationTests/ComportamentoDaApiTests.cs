using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ProjetoApi.IntegrationTests;

/// <summary>
/// Comportamentos genéricos da API, válidos para qualquer API gerada pela skill:
/// formato dos erros, limites, timeout, health, cabeçalhos e documentação.
/// Ao adaptar o template, troque só <see cref="Url"/> e <see cref="CorpoValido"/>.
/// </summary>
public sealed class ComportamentoDaApiTests(ApiFactory fabrica) : IClassFixture<ApiFactory>
{
    private const string Url = "/api/v1/transformar";
    private static readonly object CorpoValido = new { texto = "ola" };

    private HttpClient ClienteAutenticado(HttpClient? cliente = null)
    {
        cliente ??= fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-API-Key", ApiFactory.ChaveDeTeste);
        return cliente;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage resposta)
    {
        using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return documento.RootElement.Clone();
    }

    [Fact]
    public async Task Erro_401_tem_formato_padrao_e_www_authenticate()
    {
        var resposta = await fabrica.CreateClient().PostAsJsonAsync(Url, CorpoValido);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("ApiKey", resposta.Headers.WwwAuthenticate.ToString());
        var corpo = await Json(resposta);
        Assert.Equal("http_401", corpo.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(corpo.GetProperty("request_id").GetString()));
    }

    [Fact]
    public async Task Rota_inexistente_401_sem_chave_e_404_com_chave()
    {
        // Sem chave não revela se a rota existe.
        var semChave = await fabrica.CreateClient().GetAsync(new Uri("/nao-existe", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, semChave.StatusCode);

        var comChave = await ClienteAutenticado().GetAsync(new Uri("/nao-existe", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, comChave.StatusCode);
        Assert.Equal("http_404", (await Json(comChave)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Corpo_grande_demais_responde_413()
    {
        using var conteudo = new StringContent($"{{\"texto\": \"{new string('a', 70_000)}\"}}", Encoding.UTF8, "application/json");
        var resposta = await ClienteAutenticado().PostAsync(Url, conteudo);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resposta.StatusCode);
        Assert.Equal("corpo_muito_grande", (await Json(resposta)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Json_invalido_responde_400_e_nao_500()
    {
        using var conteudo = new StringContent("{nao e json", Encoding.UTF8, "application/json");
        var resposta = await ClienteAutenticado().PostAsync(Url, conteudo);
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Health_e_cabecalhos_de_seguranca()
    {
        var cliente = fabrica.CreateClient();
        var vivo = await cliente.GetAsync(new Uri("/health/live", UriKind.Relative));
        var pronto = await cliente.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, vivo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, pronto.StatusCode);
        Assert.Equal("nosniff", vivo.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", vivo.Headers.GetValues("X-Frame-Options").Single());
        Assert.StartsWith("max-age=", vivo.Headers.GetValues("Strict-Transport-Security").Single(), StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(vivo.Headers.GetValues("X-Request-ID").Single()));
    }

    [Fact]
    public async Task Rate_limit_responde_429_com_retry_after()
    {
        using var limitada = new ApiFactoryConfiguravel(new Dictionary<string, string> { ["Api:RateLimitPorMinuto"] = "2" });
        var cliente = ClienteAutenticado(limitada.CreateClient());

        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync(Url, CorpoValido)).StatusCode);
        }

        var bloqueada = await cliente.PostAsJsonAsync(Url, CorpoValido);
        Assert.Equal(HttpStatusCode.TooManyRequests, bloqueada.StatusCode);
        Assert.Equal("60", bloqueada.Headers.RetryAfter?.ToString());
        Assert.Equal("http_429", (await Json(bloqueada)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Processamento_lento_responde_504()
    {
        using var lenta = new ApiFactoryConfiguravel(
            new Dictionary<string, string> { ["Api:RequestTimeoutSeconds"] = "1" }, new ServicoFalso(atraso: TimeSpan.FromSeconds(3)));

        var resposta = await ClienteAutenticado(lenta.CreateClient()).PostAsJsonAsync(Url, CorpoValido);

        Assert.Equal(HttpStatusCode.GatewayTimeout, resposta.StatusCode);
        Assert.Equal("timeout", (await Json(resposta)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Erro_inesperado_responde_500_generico_sem_vazar_detalhes()
    {
        using var comErro = new ApiFactoryConfiguravel(
            new Dictionary<string, string>(), new ServicoFalso(lancar: new InvalidOperationException("detalhe interno: senha=abc")));

        var resposta = await ClienteAutenticado(comErro.CreateClient()).PostAsJsonAsync(Url, CorpoValido);
        var texto = await resposta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        using var json = JsonDocument.Parse(texto);
        Assert.Equal("erro_interno", json.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("request_id").GetString()));
        Assert.DoesNotContain("detalhe interno", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Documentacao_so_existe_quando_ligada_e_e_publica()
    {
        using var ligada = new ApiFactoryConfiguravel(new Dictionary<string, string> { ["Api:EnableDocs"] = "true" });
        var openApi = await ligada.CreateClient().GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        var scalar = await ligada.CreateClient().GetAsync(new Uri("/scalar", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        Assert.Contains(Url, await openApi.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, scalar.StatusCode);

        // Desligada (padrão): com chave, a rota não existe.
        var desligada = await ClienteAutenticado().GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, desligada.StatusCode);
    }
}
