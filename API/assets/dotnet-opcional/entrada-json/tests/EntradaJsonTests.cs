using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static ProjetoApi.IntegrationTests.ContratoDeErro;

namespace ProjetoApi.IntegrationTests;

/// <summary>Entrada JSON estruturada: aceita o contrato exato e rejeita todo o resto (mesmos casos do Python).</summary>
public sealed class EntradaJsonTests(ApiFactory fabrica) : IClassFixture<ApiFactory>
{
    private const string Url = "/api/v1/pedidos";

    private const string Valido = """
        {
          "id_externo": "PED-2026-0001",
          "cliente": { "nome": "  Maria da Silva  ", "email": "maria@exemplo.com" },
          "itens": [
            { "descricao": "Caneta azul", "quantidade": 2, "valor_unitario_centavos": 150 },
            { "descricao": "Caderno", "quantidade": 1, "valor_unitario_centavos": 2590 }
          ],
          "observacao": null
        }
        """;

    private async Task<HttpResponseMessage> EnviarAsync(string corpo)
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-API-Key", ApiFactory.ChaveDeTeste);
        using var conteudo = new StringContent(corpo, Encoding.UTF8, "application/json");
        return await cliente.PostAsync(new Uri(Url, UriKind.Relative), conteudo);
    }

    /// <summary>Cópia do corpo válido com um campo trocado (caminho com pontos; números = posição na lista).</summary>
    private static string Com(string caminho, JsonNode? valor)
    {
        var corpo = JsonNode.Parse(Valido)!;
        var partes = caminho.Split('.');
        var alvo = corpo;
        foreach (var parte in partes[..^1])
        {
            alvo = int.TryParse(parte, out var i) ? alvo[i]! : alvo[parte]!;
        }

        if (int.TryParse(partes[^1], out var posicao))
        {
            alvo[posicao] = valor;
        }
        else
        {
            alvo[partes[^1]] = valor;
        }

        return corpo.ToJsonString();
    }

    [Fact]
    public async Task Pedido_valido_e_totalizado_sem_devolver_dados_pessoais()
    {
        var resposta = await EnviarAsync(Valido);
        var texto = await resposta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        using var json = JsonDocument.Parse(texto);
        Assert.Equal(
            """{"id_externo":"PED-2026-0001","quantidade_itens":2,"quantidade_total":3,"valor_total_centavos":2890}""",
            json.RootElement.GetRawText());
        Assert.DoesNotContain("Maria", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("maria@", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Campos_opcionais_podem_faltar()
    {
        var corpo = JsonNode.Parse(Valido)!.AsObject();
        corpo.Remove("observacao");
        corpo["cliente"]!.AsObject().Remove("email");

        Assert.Equal(HttpStatusCode.OK, (await EnviarAsync(corpo.ToJsonString())).StatusCode);
    }

    public static TheoryData<string, string> FormasErradas => new()
    {
        { "campo_extra", "1" },                       // campo desconhecido no topo
        { "cliente.campo_extra", "1" },               // ... e aninhado
        { "itens.0.quantidade", "\"2\"" },            // texto no lugar de número: sem conversão
        { "itens.0.quantidade", "1.5" },              // decimal no lugar de inteiro
        { "itens.0.quantidade", "true" },             // booleano no lugar de número
        { "itens.0.quantidade", "0" },                // abaixo do mínimo
        { "itens.0.valor_unitario_centavos", "-1" },  // dinheiro negativo
        { "itens.0.descricao", "\"   \"" },           // vazio depois de remover os espaços
        { "cliente.nome", "\"Maria\\u0000\"" },       // caractere de controle
        { "cliente.nome", "null" },                   // nulo em campo obrigatório
        { "cliente.email", "\"sem-arroba\"" },
        { "id_externo", "\"PED 01; DROP\"" },         // fora do padrão permitido
        { "itens", "[]" },                            // lista vazia
        { "itens", "[" + string.Join(",", Enumerable.Repeat("""{"descricao":"x","quantidade":1,"valor_unitario_centavos":1}""", 101)) + "]" },
        { "cliente", "\"Maria\"" },                   // texto no lugar de objeto
    };

    [Theory]
    [MemberData(nameof(FormasErradas))]
    public async Task Forma_errada_responde_422(string caminho, string valorJson)
    {
        var resposta = await EnviarAsync(Com(caminho, JsonNode.Parse(valorJson)));
        await AfirmarProblema(resposta, HttpStatusCode.UnprocessableEntity, "entrada_invalida");
    }

    [Fact]
    public async Task Campo_obrigatorio_ausente_e_lista_no_topo_respondem_422()
    {
        var semCliente = JsonNode.Parse(Valido)!.AsObject();
        semCliente.Remove("cliente");

        await AfirmarProblema(await EnviarAsync(semCliente.ToJsonString()), HttpStatusCode.UnprocessableEntity, "entrada_invalida");
        await AfirmarProblema(await EnviarAsync("[" + Valido + "]"), HttpStatusCode.UnprocessableEntity, "entrada_invalida");
        await AfirmarProblema(await EnviarAsync("null"), HttpStatusCode.UnprocessableEntity, "entrada_invalida");
        await AfirmarProblema(await EnviarAsync(string.Empty), HttpStatusCode.UnprocessableEntity, "entrada_invalida");
    }

    [Fact]
    public async Task Json_aninhado_fundo_demais_responde_400()
    {
        var corpo = "{\"id_externo\": \"P1\", \"observacao\": " + string.Concat(Enumerable.Repeat("{\"a\": ", 9000)) + "1" + new string('}', 9001);
        await AfirmarProblema(await EnviarAsync(corpo), HttpStatusCode.BadRequest, "requisicao_invalida");
    }

    [Fact]
    public async Task Erro_nao_ecoa_a_entrada()
    {
        var resposta = await EnviarAsync(Com("cliente.nome", JsonValue.Create("<script>alert(1)</script>\u0007")));
        await AfirmarProblema(resposta, HttpStatusCode.UnprocessableEntity, "entrada_invalida");
        Assert.DoesNotContain("script", await resposta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
