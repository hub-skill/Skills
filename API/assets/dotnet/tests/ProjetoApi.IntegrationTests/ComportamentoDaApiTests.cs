using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;
using static ProjetoApi.IntegrationTests.ContratoDeErro;

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

    private static async Task<string> TextoDoJson(HttpResponseMessage resposta)
    {
        using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return documento.RootElement.GetRawText();
    }

    [Fact]
    public async Task Erro_401_tem_formato_padrao_e_www_authenticate()
    {
        var resposta = await fabrica.CreateClient().PostAsJsonAsync(Url, CorpoValido);

        await AfirmarProblema(resposta, HttpStatusCode.Unauthorized, "http_401");
        Assert.Equal("APIKey", resposta.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Rota_inexistente_401_sem_chave_e_404_com_chave()
    {
        // Sem chave não revela se a rota existe.
        var semChave = await fabrica.CreateClient().GetAsync(new Uri("/nao-existe", UriKind.Relative));
        await AfirmarProblema(semChave, HttpStatusCode.Unauthorized, "http_401");
        Assert.Equal("APIKey", semChave.Headers.WwwAuthenticate.ToString());

        var comChave = await ClienteAutenticado().GetAsync(new Uri("/nao-existe", UriKind.Relative));
        await AfirmarProblema(comChave, HttpStatusCode.NotFound, "http_404");
    }

    [Fact]
    public async Task Metodo_errado_401_sem_chave_e_405_com_chave()
    {
        var semChave = await fabrica.CreateClient().GetAsync(new Uri(Url, UriKind.Relative));
        await AfirmarProblema(semChave, HttpStatusCode.Unauthorized, "http_401");

        var comChave = await ClienteAutenticado().GetAsync(new Uri(Url, UriKind.Relative));
        await AfirmarProblema(comChave, HttpStatusCode.MethodNotAllowed, "http_405");
    }

    [Fact]
    public async Task Corpo_grande_demais_responde_413()
    {
        using var conteudo = new StringContent($"{{\"texto\": \"{new string('a', 70_000)}\"}}", Encoding.UTF8, "application/json");
        var resposta = await ClienteAutenticado().PostAsync(Url, conteudo);

        await AfirmarProblema(resposta, HttpStatusCode.RequestEntityTooLarge, "corpo_muito_grande");
    }

    [Fact]
    public async Task Json_invalido_responde_400_e_nao_500()
    {
        // ApiFactory roda em Production: sem ThrowOnBadRequest=true este teste falharia (400 fora do formato).
        using var conteudo = new StringContent("{nao e json", Encoding.UTF8, "application/json");
        var resposta = await ClienteAutenticado().PostAsync(Url, conteudo);

        await AfirmarProblema(resposta, HttpStatusCode.BadRequest, "requisicao_invalida");
    }

    [Fact]
    public async Task Json_aninhado_fundo_demais_responde_400_e_nao_500()
    {
        // ~45 KB (abaixo do limite de corpo) num campo extra: o leitor percorre o valor e passa da profundidade máxima.
        var corpo = "{\"texto\": \"ola\", \"extra\": " + string.Concat(Enumerable.Repeat("{\"a\": ", 9000)) + "1" + new string('}', 9001);
        using var conteudo = new StringContent(corpo, Encoding.UTF8, "application/json");
        var resposta = await ClienteAutenticado().PostAsync(Url, conteudo);

        await AfirmarProblema(resposta, HttpStatusCode.BadRequest, "requisicao_invalida");
    }

    [Theory]
    [InlineData("{\"texto\": 1}")]
    [InlineData("[\"ola\"]")]
    [InlineData("{}")]
    [InlineData("{\"texto\": null}")]
    public async Task Json_valido_com_forma_errada_responde_422(string corpo)
    {
        // Igual ao template Python: só o corpo ilegível é 400; JSON válido com forma errada é 422.
        using var conteudo = new StringContent(corpo, Encoding.UTF8, "application/json");
        var resposta = await ClienteAutenticado().PostAsync(Url, conteudo);

        await AfirmarProblema(resposta, HttpStatusCode.UnprocessableEntity, "entrada_invalida");
    }

    [Fact]
    public async Task Health_e_cabecalhos_de_seguranca()
    {
        var cliente = fabrica.CreateClient();
        var vivo = await cliente.GetAsync(new Uri("/health/live", UriKind.Relative));
        var pronto = await cliente.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, vivo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, pronto.StatusCode);
        Assert.Equal("""{"status":"ok"}""", await TextoDoJson(vivo));
        Assert.Equal("""{"status":"ok"}""", await TextoDoJson(pronto));
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
        await AfirmarProblema(bloqueada, HttpStatusCode.TooManyRequests, "limite_excedido");
        Assert.Equal("60", bloqueada.Headers.RetryAfter?.ToString());

        // Com o limite estourado, health e métricas continuam respondendo: no Kubernetes as probes saem todas do
        // IP do nó, e um 429 na liveness reiniciaria o pod.
        foreach (var rota in new[] { "/health/live", "/health/ready", "/metrics" })
        {
            Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync(new Uri(rota, UriKind.Relative))).StatusCode);
        }
    }

    [Fact]
    public async Task Processamento_lento_responde_504()
    {
        using var lenta = new ApiFactoryConfiguravel(
            new Dictionary<string, string> { ["Api:RequestTimeoutSeconds"] = "1" }, new ServicoFalso(atraso: TimeSpan.FromSeconds(3)));

        var resposta = await ClienteAutenticado(lenta.CreateClient()).PostAsJsonAsync(Url, CorpoValido);

        await AfirmarProblema(resposta, HttpStatusCode.GatewayTimeout, "timeout");
    }

    [Fact]
    public async Task Erro_inesperado_responde_500_generico_sem_vazar_detalhes()
    {
        using var comErro = new ApiFactoryConfiguravel(
            new Dictionary<string, string>(), new ServicoFalso(lancar: new InvalidOperationException("detalhe interno: senha=abc")));

        var resposta = await ClienteAutenticado(comErro.CreateClient()).PostAsJsonAsync(Url, CorpoValido);
        var texto = await resposta.Content.ReadAsStringAsync();

        await AfirmarProblema(resposta, HttpStatusCode.InternalServerError, "erro_interno");
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

        // Desligada (padrão): com chave, a rota não existe; sem chave, 401.
        var desligada = await ClienteAutenticado().GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        await AfirmarProblema(desligada, HttpStatusCode.NotFound, "http_404");
        var semChave = await fabrica.CreateClient().GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        await AfirmarProblema(semChave, HttpStatusCode.Unauthorized, "http_401");
    }
}
