using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjetoApi.Movimentacao;
using ProjetoApi.Services;
using Xunit;

namespace ProjetoApi.IntegrationTests;

/// <summary>
/// Movimentação: cada requisição com chave de API válida (menos /health e /metrics) ganha um ULID na chegada e é gravada, com a
/// resposta e o erro (se houver), em segundo plano. Sem banco: repositório falso.
/// </summary>
public sealed class MovimentacaoApiTests : IClassFixture<MovimentacaoFactory>
{
    private const string Url = "/api/v1/transformar";
    private readonly MovimentacaoFactory _fabrica;

    public MovimentacaoApiTests(MovimentacaoFactory fabrica)
    {
        _fabrica = fabrica;
        _fabrica.Movimentacao.Limpar();
    }

    private static HttpClient Autenticado(MovimentacaoFactory fabrica)
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-API-Key", MovimentacaoFactory.Chave);
        return cliente;
    }

    private async Task<HttpResponseMessage> EnviarAsync(string corpo)
    {
        using var conteudo = new StringContent(corpo, Encoding.UTF8, "application/json");
        return await Autenticado(_fabrica).PostAsync(new Uri(Url, UriKind.Relative), conteudo);
    }

    [Fact]
    public async Task Requisicao_com_sucesso_ganha_ulid_e_e_gravada_com_a_resposta()
    {
        var resposta = await Autenticado(_fabrica).PostAsJsonAsync(Url, new { texto = "  ola  " });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var id = MovimentacaoFalsa.IdDoHeader(resposta);
        var gravada = await _fabrica.Movimentacao.EsperarAsync(id);
        Assert.Equal(("POST", Url, 200), (gravada.Metodo, gravada.Rota, gravada.StatusHttp));
        Assert.Contains("ola", gravada.RequisicaoConteudo, StringComparison.Ordinal);
        Assert.Equal(("OK", "OK"), (gravada.SituacaoRequisicao, gravada.SituacaoResposta));
        Assert.Equal(await resposta.Content.ReadAsStringAsync(), gravada.RespostaConteudo); // exatamente o que o cliente recebeu
        Assert.Null(gravada.Erro);
        Assert.True(gravada.RespostaEm >= gravada.RequisicaoEm);
        Assert.InRange((gravada.RequisicaoEm - Ulid.Momento(id)).TotalMilliseconds, -2, 2); // o ULID carrega a hora de chegada
    }

    [Fact]
    public async Task Cpf_e_mascarado_na_requisicao_e_na_resposta()
    {
        var resposta = await Autenticado(_fabrica).PostAsJsonAsync(Url, new { texto = "cpf 529.982.247-25" });

        var gravada = await _fabrica.Movimentacao.EsperarAsync(MovimentacaoFalsa.IdDoHeader(resposta));
        Assert.Contains("***.982.247-**", gravada.RequisicaoConteudo, StringComparison.Ordinal);
        Assert.DoesNotContain("529.982.247-25", gravada.RequisicaoConteudo, StringComparison.Ordinal);
        Assert.Contains("***.982.247-**", gravada.RespostaConteudo, StringComparison.Ordinal);
        Assert.DoesNotContain("529.982.247-25", gravada.RespostaConteudo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conteudo_da_requisicao_aceita_qualquer_formato()
    {
        foreach (var corpo in new[] { "{nao e json", "[\"lista\"]", "<xml><a>1</a></xml>" })
        {
            var gravada = await _fabrica.Movimentacao.EsperarAsync(MovimentacaoFalsa.IdDoHeader(await EnviarAsync(corpo)));
            Assert.Equal(corpo, gravada.RequisicaoConteudo);
        }

        // Byte inválido em UTF-8 não derruba a gravação: vira U+FFFD.
        using var invalido = new ByteArrayContent([.. "texto solto "u8, 0xFF, .. " fim"u8]);
        invalido.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var resposta = await Autenticado(_fabrica).PostAsync(new Uri(Url, UriKind.Relative), invalido);
        var ultima = await _fabrica.Movimentacao.EsperarAsync(MovimentacaoFalsa.IdDoHeader(resposta));
        Assert.Equal("texto solto � fim", ultima.RequisicaoConteudo);
    }

    [Theory]
    [InlineData("entrada_invalida", 422, "ER", "ER")]
    [InlineData("rota_inexistente", 404, "ER", "ER")]
    [InlineData("json_malformado", 400, "ER", "ER")]
    [InlineData("sucesso", 200, "OK", "OK")]
    public async Task Situacoes_da_requisicao_e_da_resposta(string chamada, int status, string situacaoRequisicao, string situacaoResposta)
    {
        var resposta = chamada switch
        {
            "entrada_invalida" => await Autenticado(_fabrica).PostAsJsonAsync(Url, new { texto = "   " }),
            "rota_inexistente" => await Autenticado(_fabrica).GetAsync(new Uri("/nao-existe", UriKind.Relative)),
            "json_malformado" => await EnviarAsync("{nao e json"),
            _ => await Autenticado(_fabrica).PostAsJsonAsync(Url, new { texto = "ola" }),
        };

        Assert.Equal(status, (int)resposta.StatusCode);
        var gravada = await _fabrica.Movimentacao.EsperarAsync(MovimentacaoFalsa.IdDoHeader(resposta));
        Assert.Equal((status, situacaoRequisicao, situacaoResposta), (gravada.StatusHttp, gravada.SituacaoRequisicao, gravada.SituacaoResposta));
    }

    [Fact]
    public async Task Erro_nao_tratado_entra_na_movimentacao_com_os_cabecalhos_da_api_e_o_erro_junto()
    {
        using var comErro = new MovimentacaoFactory(servico: new ServicoFalso(lancar: new InvalidOperationException("detalhe interno: senha=abc")));
        var resposta = await Autenticado(comErro).PostAsJsonAsync(Url, new { texto = "ola" });

        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        Assert.True(resposta.Headers.Contains("X-Requisicao") && resposta.Headers.Contains("X-Request-ID"));
        Assert.Equal("nosniff", resposta.Headers.GetValues("X-Content-Type-Options").Single());
        var gravada = await comErro.Movimentacao.EsperarAsync(MovimentacaoFalsa.IdDoHeader(resposta));
        Assert.Equal((500, "OK", "ER"), (gravada.StatusHttp, gravada.SituacaoRequisicao, gravada.SituacaoResposta));
        Assert.DoesNotContain("senha", gravada.RespostaConteudo, StringComparison.Ordinal);

        var erro = Assert.IsType<RegistroDeErro>(gravada.Erro);
        Assert.Equal(("erro_interno", "System.InvalidOperationException"), (erro.Codigo, erro.TipoExcecao));
        Assert.DoesNotContain("senha", erro.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("POST", Url, null)] // sem chave
    [InlineData("POST", Url, "chave-errada")]
    [InlineData("GET", "/nao-existe", null)] // sem chave: 401, não revela se a rota existe
    [InlineData("GET", Url, null)]
    public async Task Sem_chave_valida_nao_entra_na_movimentacao(string metodo, string rota, string? chave)
    {
        // Varreduras e chamadas sem chave recebem 401 e não gravam nada.
        var cliente = _fabrica.CreateClient();
        if (chave is not null)
        {
            cliente.DefaultRequestHeaders.Add("X-API-Key", chave);
        }

        using var pedido = new HttpRequestMessage(new HttpMethod(metodo), new Uri(rota, UriKind.Relative))
        {
            Content = JsonContent.Create(new { texto = "ola" }),
        };
        var resposta = await cliente.SendAsync(pedido);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.False(resposta.Headers.Contains("X-Requisicao"));
        Assert.True(resposta.Headers.Contains("X-Request-ID")); // o 401 continua com request_id e headers de segurança

        // Uma chamada de marcação, já com chave: quando ela é gravada, qualquer gravação anterior já teria chegado.
        var marca = await Autenticado(_fabrica).PostAsJsonAsync(Url, new { texto = "ola" });
        await _fabrica.Movimentacao.EsperarAsync(MovimentacaoFalsa.IdDoHeader(marca));
        Assert.DoesNotContain(_fabrica.Movimentacao.Registros, r => r.Erro?.RequestId == resposta.Headers.GetValues("X-Request-ID").Single());
        Assert.DoesNotContain(_fabrica.Movimentacao.Registros, r => r.StatusHttp == 401);
    }

    [Fact]
    public async Task Health_e_metrics_nao_entram_na_movimentacao()
    {
        var antes = _fabrica.Movimentacao.Registros.Count;
        var cliente = _fabrica.CreateClient();
        foreach (var rota in new[] { "/health/live", "/health/ready", "/metrics" })
        {
            var resposta = await cliente.GetAsync(new Uri(rota, UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            Assert.False(resposta.Headers.Contains("X-Requisicao"));
        }

        Assert.Equal(antes, _fabrica.Movimentacao.Registros.Count);
    }

    [Fact]
    public async Task Cada_erro_e_gravado_junto_com_a_sua_movimentacao_sem_o_corpo()
    {
        var primeira = await Autenticado(_fabrica).PostAsJsonAsync(Url, new { texto = "   " });
        var segunda = await Autenticado(_fabrica).GetAsync(new Uri("/nao-existe", UriKind.Relative));

        foreach (var resposta in new[] { primeira, segunda })
        {
            var gravada = await _fabrica.Movimentacao.EsperarAsync(MovimentacaoFalsa.IdDoHeader(resposta));
            var erro = Assert.IsType<RegistroDeErro>(gravada.Erro);
            Assert.Equal(resposta.Headers.GetValues("X-Request-ID").Single(), erro.RequestId);
            Assert.Equal(gravada.StatusHttp, erro.Status);
            Assert.True(erro.DuracaoMs >= 0);
            Assert.DoesNotContain("texto", erro.ToString(), StringComparison.Ordinal); // nunca o corpo da requisição
        }
    }

    [Fact]
    public async Task Limite_de_requisicoes_estourado_tambem_entra_na_movimentacao()
    {
        using var limitada = new MovimentacaoFactory(configuracao: new Dictionary<string, string> { ["Api:RateLimitPorMinuto"] = "1" });
        var cliente = Autenticado(limitada);
        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync(Url, new { texto = "ola" })).StatusCode);

        var bloqueada = await cliente.PostAsJsonAsync(Url, new { texto = "ola" });

        Assert.Equal(HttpStatusCode.TooManyRequests, bloqueada.StatusCode);
        var gravada = await limitada.Movimentacao.EsperarAsync(MovimentacaoFalsa.IdDoHeader(bloqueada));
        Assert.Equal(("ER", "ER"), (gravada.SituacaoRequisicao, gravada.SituacaoResposta));
        Assert.Equal("limite_excedido", gravada.Erro?.Codigo);
    }

    [Fact]
    public async Task Corpo_acima_do_limite_registra_so_o_tamanho()
    {
        var corpo = "{\"texto\": \"" + new string('1', 70_000) + "\"}";
        var resposta = await EnviarAsync(corpo);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resposta.StatusCode);
        var conteudo = (await _fabrica.Movimentacao.EsperarAsync(MovimentacaoFalsa.IdDoHeader(resposta))).RequisicaoConteudo;
        Assert.Contains(corpo.Length.ToString(CultureInfo.InvariantCulture), conteudo, StringComparison.Ordinal);
        Assert.DoesNotContain("1111", conteudo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Banco_fora_ou_lento_na_gravacao_nao_afeta_a_resposta()
    {
        // Banco lento: a gravação fica presa num portão (sem cronômetro: o teste não depende da velocidade da máquina).
        using var lenta = new MovimentacaoFactory();
        lenta.Movimentacao.Portao = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var resposta = await Autenticado(lenta).PostAsJsonAsync(Url, new { texto = "ola" });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode); // a resposta saiu com a gravação ainda presa
        var id = MovimentacaoFalsa.IdDoHeader(resposta); // o número nasce na API: sempre existe
        Assert.DoesNotContain(lenta.Movimentacao.Registros, r => r.RequisicaoId == id);
        lenta.Movimentacao.Portao.SetResult();
        Assert.Equal(id, (await lenta.Movimentacao.EsperarAsync(id)).RequisicaoId); // solto o portão, a gravação acontece

        // Banco fora: a gravação falha, e a resposta continua saindo normalmente.
        using var fora = new MovimentacaoFactory();
        fora.Movimentacao.Falha = true;
        var semBanco = await Autenticado(fora).PostAsJsonAsync(Url, new { texto = "ola" });
        Assert.Equal(HttpStatusCode.OK, semBanco.StatusCode);
        Assert.True(Ulid.Valido(semBanco.Headers.GetValues("X-Requisicao").Single()));
    }

    [Fact]
    public async Task Cada_requisicao_tem_um_ulid_proprio()
    {
        var ids = new List<string>();
        for (var i = 0; i < 30; i++)
        {
            ids.Add(MovimentacaoFalsa.IdDoHeader(await Autenticado(_fabrica).PostAsJsonAsync(Url, new { texto = "ola" })));
        }

        Assert.Equal(30, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.InRange((DateTimeOffset.UtcNow - Ulid.Momento(id)).TotalSeconds, -1, 30));
    }
}

/// <summary>Sobe a API em memória com a movimentação falsa: os testes nunca tocam o banco.</summary>
public sealed class MovimentacaoFactory : WebApplicationFactory<Program>
{
    public const string Chave = "chave-de-teste-com-pelo-menos-32-caracteres";

    private readonly IReadOnlyDictionary<string, string>? _configuracao;
    private readonly ITransformacaoService? _servico;

    /// <summary>Único construtor público, sem parâmetros: é o que o xUnit aceita para <c>IClassFixture</c>.</summary>
    public MovimentacaoFactory()
    {
    }

    internal MovimentacaoFactory(IReadOnlyDictionary<string, string>? configuracao = null, ITransformacaoService? servico = null)
    {
        _configuracao = configuracao;
        _servico = servico;
    }

    public MovimentacaoFalsa Movimentacao { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Production");
        builder.UseSetting("Api:ApiKey", Chave);
        builder.UseSetting("Api:RateLimitPorMinuto", "10000");
        foreach (var (chave, valor) in _configuracao ?? new Dictionary<string, string>())
        {
            builder.UseSetting(chave, valor);
        }

        builder.ConfigureTestServices(servicos =>
        {
            // Troque também os repositórios reais de consulta (módulo bancos), se a API os tiver.
            servicos.RemoveAll<IRepositorioDeMovimentacao>();
            servicos.AddSingleton<IRepositorioDeMovimentacao>(Movimentacao);
            servicos.RemoveAll<MovimentacaoOpcoes>();
            servicos.AddSingleton(new MovimentacaoOpcoes(64 * 1024));
            if (_servico is not null)
            {
                servicos.RemoveAll<ITransformacaoService>();
                servicos.AddSingleton(_servico);
            }
        });
    }
}

/// <summary>Repositório falso: guarda as movimentações na memória; pode falhar ou demorar, como um banco com problema.</summary>
public sealed class MovimentacaoFalsa : IRepositorioDeMovimentacao
{
    private readonly ConcurrentDictionary<string, RegistroDeMovimentacao> _registros = new();

    /// <summary>Simula o banco fora: a gravação falha.</summary>
    public bool Falha { get; set; }

    /// <summary>Simula o banco lento: enquanto o portão não for liberado, a gravação espera.</summary>
    public TaskCompletionSource? Portao { get; set; }

    public IReadOnlyList<RegistroDeMovimentacao> Registros => [.. _registros.Values];

    public void Limpar()
    {
        Falha = false;
        Portao = null;
    }

    public async Task GravarAsync(IReadOnlyList<RegistroDeMovimentacao> registros, CancellationToken cancellationToken)
    {
        if (Falha)
        {
            throw new InvalidOperationException("banco fora");
        }

        if (Portao is { } portao)
        {
            await portao.Task.WaitAsync(cancellationToken);
        }

        foreach (var r in registros)
        {
            _registros[r.RequisicaoId] = r;
        }
    }

    /// <summary>Espera (até 5 s) a movimentação gravada desta requisição: a gravação é em segundo plano.</summary>
    public async Task<RegistroDeMovimentacao> EsperarAsync(string requisicaoId)
    {
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            if (_registros.TryGetValue(requisicaoId, out var registro))
            {
                return registro;
            }

            await Task.Delay(10, prazo.Token);
        }
    }

    /// <summary>ULID da requisição a partir do header X-Requisicao.</summary>
    public static string IdDoHeader(HttpResponseMessage resposta)
    {
        ArgumentNullException.ThrowIfNull(resposta);
        var id = resposta.Headers.GetValues(MovimentacaoMiddleware.Header).Single();
        Assert.True(Ulid.Valido(id), $"X-Requisicao não é um ULID: {id}");
        return id;
    }
}
