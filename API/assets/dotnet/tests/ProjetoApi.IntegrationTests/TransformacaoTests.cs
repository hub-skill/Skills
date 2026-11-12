using System.Net;
using System.Net.Http.Json;
using ProjetoApi.Contracts;
using Xunit;
using static ProjetoApi.IntegrationTests.ContratoDeErro;

namespace ProjetoApi.IntegrationTests;

public sealed class TransformacaoTests(ApiFactory fabrica) : IClassFixture<ApiFactory>
{
    private const string Url = "/api/v1/transformar";

    private HttpClient ClienteAutenticado()
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-API-Key", ApiFactory.ChaveDeTeste);
        return cliente;
    }

    [Fact]
    public async Task Transforma_texto_e_remove_espacos()
    {
        var resposta = await ClienteAutenticado().PostAsJsonAsync(Url, new { texto = "  ola  " });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<SaidaTexto>();
        Assert.Equal("OLA", corpo!.Resultado);
    }

    [Fact]
    public async Task Aceita_unicode_e_tamanho_maximo()
    {
        var resposta = await ClienteAutenticado().PostAsJsonAsync(Url, new { texto = new string('ç', 10_000) });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<SaidaTexto>();
        Assert.Equal(new string('Ç', 10_000), corpo!.Resultado);
    }

    [Fact]
    public async Task Rejeita_sem_chave()
    {
        var resposta = await fabrica.CreateClient().PostAsJsonAsync(Url, new { texto = "ola" });
        await AfirmarProblema(resposta, HttpStatusCode.Unauthorized, "http_401");
    }

    [Fact]
    public async Task Rejeita_chave_errada()
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-API-Key", "errada");

        var resposta = await cliente.PostAsJsonAsync(Url, new { texto = "ola" });
        await AfirmarProblema(resposta, HttpStatusCode.Unauthorized, "http_401");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ola\u0000mundo")]
    [InlineData("ola\u0007")]
    public async Task Rejeita_entrada_invalida(string texto)
    {
        var resposta = await ClienteAutenticado().PostAsJsonAsync(Url, new { texto });
        await AfirmarProblema(resposta, HttpStatusCode.UnprocessableEntity, "entrada_invalida");
    }

    [Fact]
    public async Task Rejeita_acima_do_tamanho_maximo()
    {
        var resposta = await ClienteAutenticado().PostAsJsonAsync(Url, new { texto = new string('a', 10_001) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
    }

    [Fact]
    public async Task Nao_ecoa_entrada_maliciosa_no_erro()
    {
        const string payload = "'; DROP TABLE usuarios; --\u0000";
        var resposta = await ClienteAutenticado().PostAsJsonAsync(Url, new { texto = payload });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        var corpo = await resposta.Content.ReadAsStringAsync();
        Assert.DoesNotContain(payload, corpo);
    }
}
