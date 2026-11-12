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
/// Movimentação: cada requisição com chave de API válida (menos /health e /metrics) é numerada na chegada e
/// completada com a resposta; os erros vão para a log_erros com a chave. Sem banco: repositórios falsos.
/// </summary>
public sealed class MovimentacaoApiTests : IClassFixture<MovimentacaoFactory>
{
    private const string Url = "/api/v1/transformar";
    private readonly MovimentacaoFactory _fabrica;
    private readonly int _inicioDoTeste;

    public MovimentacaoApiTests(MovimentacaoFactory fabrica)
    {
        _fabrica = fabrica;
        _fabrica.Movimentacao.Limpar();
        _inicioDoTeste = _fabrica.Movimentacao.TotalDeChegadas;
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

    private static string IdDaResposta(HttpResponseMessage resposta) => resposta.Headers.GetValues("X-Request-ID").Single();

    private static async Task<string> RequestId(HttpResponseMessage resposta)
    {
        using var corpo = System.Text.Json.JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return corpo.RootElement.GetProperty("request_id").GetString()!;
    }

    [Fact]
    public async Task Requisicao_com_sucesso_e_numerada_e_a_resposta_e_gravada()
    {
        var resposta = await Autenticado(_fabrica).PostAsJsonAsync(Url, new { texto = "  ola  " });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var numero = MovimentacaoFalsa.NumeroDoHeader(resposta);
        var chegada = _fabrica.Movimentacao.ChegadaDe(numero)!;
        Assert.Equal(("POST", Url), (chegada.Metodo, chegada.Rota));
        Assert.Contains("ola", chegada.Conteudo, StringComparison.Ordinal);

        var gravada = await _fabrica.Movimentacao.EsperarRespostaAsync(numero);
        Assert.Equal((MovimentacaoFalsa.Ano, numero, 200), (gravada.Ano, gravada.Requisicao, gravada.StatusHttp));
        Assert.Equal(("OK", "OK"), (gravada.SituacaoRequisicao, gravada.SituacaoResposta));
        Assert.Equal(await resposta.Content.ReadAsStringAsync(), gravada.Conteudo); // exatamente o que o cliente recebeu
    }

    [Fact]
    public async Task Cpf_e_mascarado_na_requisicao_e_na_resposta()
    {
        var resposta = await Autenticado(_fabrica).PostAsJsonAsync(Url, new { texto = "cpf 529.982.247-25" });
        var numero = MovimentacaoFalsa.NumeroDoHeader(resposta);

        var chegada = _fabrica.Movimentacao.ChegadaDe(numero)!;
        Assert.Contains("***.982.247-**", chegada.Conteudo, StringComparison.Ordinal);
        Assert.DoesNotContain("529.982.247-25", chegada.Conteudo, StringComparison.Ordinal);
        var gravada = await _fabrica.Movimentacao.EsperarRespostaAsync(numero);
        Assert.Contains("***.982.247-**", gravada.Conteudo, StringComparison.Ordinal);
        Assert.DoesNotContain("529.982.247-25", gravada.Conteudo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conteudo_da_requisicao_aceita_qualquer_formato()
    {
        foreach (var corpo in new[] { "{nao e json", "[\"lista\"]", "<xml><a>1</a></xml>" })
        {
            var numero = MovimentacaoFalsa.NumeroDoHeader(await EnviarAsync(corpo));
            Assert.Equal(corpo, _fabrica.Movimentacao.ChegadaDe(numero)!.Conteudo);
        }

        // Byte inválido em UTF-8 não derruba a gravação: vira U+FFFD.
        using var invalido = new ByteArrayContent([.. "texto solto "u8, 0xFF, .. " fim"u8]);
        invalido.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var resposta = await Autenticado(_fabrica).PostAsync(new Uri(Url, UriKind.Relative), invalido);
        Assert.Equal("texto solto � fim", _fabrica.Movimentacao.ChegadaDe(MovimentacaoFalsa.NumeroDoHeader(resposta))!.Conteudo);
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
        var gravada = await _fabrica.Movimentacao.EsperarRespostaAsync(MovimentacaoFalsa.NumeroDoHeader(resposta));
        Assert.Equal((status, situacaoRequisicao, situacaoResposta), (gravada.StatusHttp, gravada.SituacaoRequisicao, gravada.SituacaoResposta));
    }

    [Fact]
    public async Task Erro_nao_tratado_entra_na_movimentacao_com_os_cabecalhos_da_api()
    {
        using var comErro = new MovimentacaoFactory(servico: new ServicoFalso(lancar: new InvalidOperationException("detalhe interno: senha=abc")));
        var resposta = await Autenticado(comErro).PostAsJsonAsync(Url, new { texto = "ola" });

        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        Assert.True(resposta.Headers.Contains("X-Requisicao") && resposta.Headers.Contains("X-Request-ID"));
        Assert.Equal("nosniff", resposta.Headers.GetValues("X-Content-Type-Options").Single());
        var gravada = await comErro.Movimentacao.EsperarRespostaAsync(MovimentacaoFalsa.NumeroDoHeader(resposta));
        Assert.Equal((500, "OK", "ER"), (gravada.StatusHttp, gravada.SituacaoRequisicao, gravada.SituacaoResposta));
        Assert.DoesNotContain("senha", gravada.Conteudo, StringComparison.Ordinal);

        var erro = await comErro.Erros.EsperarAsync(await RequestId(resposta));
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
        // Varreduras e chamadas sem chave recebem 401 e não gravam nada: nem movimentação, nem log de erros.
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
        await _fabrica.Movimentacao.EsperarRespostaAsync(MovimentacaoFalsa.NumeroDoHeader(marca));
        Assert.Equal(1, _fabrica.Movimentacao.TotalDeChegadas - _inicioDoTeste);
        Assert.DoesNotContain(_fabrica.Erros.Registros, r => r.RequestId == IdDaResposta(resposta));
    }

    [Fact]
    public async Task Health_e_metrics_nao_entram_na_movimentacao()
    {
        var cliente = _fabrica.CreateClient();
        foreach (var rota in new[] { "/health/live", "/health/ready", "/metrics" })
        {
            var resposta = await cliente.GetAsync(new Uri(rota, UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            Assert.False(resposta.Headers.Contains("X-Requisicao"));
        }

        Assert.Equal(_inicioDoTeste, _fabrica.Movimentacao.TotalDeChegadas);
    }

    [Fact]
    public async Task Cada_erro_vai_para_o_log_com_a_chave_da_sua_movimentacao()
    {
        var primeira = await Autenticado(_fabrica).PostAsJsonAsync(Url, new { texto = "   " });
        var segunda = await Autenticado(_fabrica).GetAsync(new Uri("/nao-existe", UriKind.Relative));

        foreach (var resposta in new[] { primeira, segunda })
        {
            var registro = await _fabrica.Erros.EsperarAsync(await RequestId(resposta));
            Assert.Equal((MovimentacaoFalsa.Ano, MovimentacaoFalsa.NumeroDoHeader(resposta)), (registro.Ano, registro.Requisicao));
            Assert.True(registro.DuracaoMs >= 0);
            Assert.DoesNotContain("texto", registro.ToString(), StringComparison.Ordinal); // nunca o corpo da requisição
        }
    }

    [Fact]
    public async Task Sucesso_nao_vai_para_o_log_de_erros()
    {
        var resposta = await Autenticado(_fabrica).PostAsJsonAsync(Url, new { texto = "ola" });
        await _fabrica.Movimentacao.EsperarRespostaAsync(MovimentacaoFalsa.NumeroDoHeader(resposta));

        // Os registros do log são pelo request_id: erros atrasados de outros testes não contam.
        Assert.DoesNotContain(_fabrica.Erros.Registros, r => r.RequestId == IdDaResposta(resposta));
    }

    [Fact]
    public async Task Limite_de_requisicoes_estourado_tambem_entra_na_movimentacao()
    {
        using var limitada = new MovimentacaoFactory(configuracao: new Dictionary<string, string> { ["Api:RateLimitPorMinuto"] = "1" });
        var cliente = Autenticado(limitada);
        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync(Url, new { texto = "ola" })).StatusCode);

        var bloqueada = await cliente.PostAsJsonAsync(Url, new { texto = "ola" });

        Assert.Equal(HttpStatusCode.TooManyRequests, bloqueada.StatusCode);
        var gravada = await limitada.Movimentacao.EsperarRespostaAsync(MovimentacaoFalsa.NumeroDoHeader(bloqueada));
        Assert.Equal(("ER", "ER"), (gravada.SituacaoRequisicao, gravada.SituacaoResposta));
        var erro = await limitada.Erros.EsperarAsync(await RequestId(bloqueada));
        Assert.Equal("limite_excedido", erro.Codigo);
    }

    [Fact]
    public async Task Corpo_acima_do_limite_registra_so_o_tamanho()
    {
        var corpo = "{\"texto\": \"" + new string('1', 70_000) + "\"}";
        var resposta = await EnviarAsync(corpo);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resposta.StatusCode);
        var conteudo = _fabrica.Movimentacao.ChegadaDe(MovimentacaoFalsa.NumeroDoHeader(resposta))!.Conteudo;
        Assert.Contains(corpo.Length.ToString(CultureInfo.InvariantCulture), conteudo, StringComparison.Ordinal);
        Assert.DoesNotContain("1111", conteudo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sem_numero_a_requisicao_segue_e_o_erro_nao_e_gravado()
    {
        _fabrica.Movimentacao.NumeracaoFalha = true;
        var ok = await Autenticado(_fabrica).PostAsJsonAsync(Url, new { texto = "ola" });
        var erro = await Autenticado(_fabrica).PostAsJsonAsync(Url, new { texto = "   " });
        _fabrica.Movimentacao.NumeracaoFalha = false;

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity), (ok.StatusCode, erro.StatusCode));
        Assert.False(ok.Headers.Contains("X-Requisicao") || erro.Headers.Contains("X-Requisicao"));
        Assert.Equal(_inicioDoTeste, _fabrica.Movimentacao.TotalDeChegadas);

        // Marcação, já com número: quando ela é gravada, qualquer gravação anterior já teria chegado.
        var marca = await Autenticado(_fabrica).GetAsync(new Uri("/nao-existe", UriKind.Relative));
        await _fabrica.Erros.EsperarAsync(await RequestId(marca));
        Assert.DoesNotContain(_fabrica.Erros.Registros, r => r.RequestId == IdDaResposta(ok) || r.RequestId == IdDaResposta(erro));
    }

    [Fact]
    public async Task Banco_lento_na_chegada_nao_segura_a_requisicao_alem_do_prazo()
    {
        _fabrica.Movimentacao.NumeracaoAtraso = TimeSpan.FromSeconds(10);

        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        var resposta = await Autenticado(_fabrica).PostAsJsonAsync(Url, new { texto = "ola" });
        cronometro.Stop();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.False(resposta.Headers.Contains("X-Requisicao"));
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(5), $"demorou {cronometro.Elapsed}");
        Assert.Equal(_inicioDoTeste, _fabrica.Movimentacao.TotalDeChegadas); // a gravação lenta foi abandonada
    }
}

/// <summary>Sobe a API em memória com a movimentação e o log de erros falsos: os testes nunca tocam o banco.</summary>
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

    public DestinoDeErrosFalso Erros { get; } = new();

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
            servicos.RemoveAll<IRepositorioDeMovimentacao>();
            servicos.AddSingleton<IRepositorioDeMovimentacao>(Movimentacao);
            servicos.RemoveAll<IRepositorioDeErros>();
            servicos.AddSingleton<IRepositorioDeErros>(Erros);
            servicos.RemoveAll<MovimentacaoOpcoes>();
            servicos.AddSingleton(new MovimentacaoOpcoes(Movimentacao.PrazoParaNumerar, 64 * 1024));
            if (_servico is not null)
            {
                servicos.RemoveAll<ITransformacaoService>();
                servicos.AddSingleton(_servico);
            }
        });
    }
}

/// <summary>
/// Movimentação falsa: numera na memória (ano 2026) e guarda a chegada e a resposta de cada requisição. A numeração
/// nunca recomeça: respostas atrasadas de um teste anterior não colidem com as do seguinte (cada teste acha a sua
/// pelo header X-Requisicao).
/// </summary>
public sealed class MovimentacaoFalsa : IRepositorioDeMovimentacao
{
    public const int Ano = 2026;

    private readonly ConcurrentDictionary<long, Chegada> _chegadas = new();
    private readonly ConcurrentDictionary<long, RegistroDeResposta> _respostas = new();
    private long _ultimo;

    public sealed record Chegada(string Metodo, string Rota, string? Conteudo);

    /// <summary>Simula o banco fora na chegada: a numeração falha.</summary>
    public bool NumeracaoFalha { get; set; }

    /// <summary>Simula o banco lento na chegada.</summary>
    public TimeSpan NumeracaoAtraso { get; set; }

    /// <summary>Prazo que a API espera pelo número (curto, para o teste do banco lento não demorar).</summary>
    public TimeSpan PrazoParaNumerar { get; } = TimeSpan.FromMilliseconds(300);

    public int TotalDeChegadas => _chegadas.Count;

    public void Limpar()
    {
        NumeracaoFalha = false;
        NumeracaoAtraso = TimeSpan.Zero;
    }

    public async Task<(int Ano, long Requisicao)> RegistrarRequisicaoAsync(
        string metodo, string rota, string? conteudo, CancellationToken cancellationToken)
    {
        if (NumeracaoFalha)
        {
            throw new InvalidOperationException("banco fora");
        }

        if (NumeracaoAtraso > TimeSpan.Zero)
        {
            await Task.Delay(NumeracaoAtraso, cancellationToken);
        }

        var numero = Interlocked.Increment(ref _ultimo);
        _chegadas[numero] = new Chegada(metodo, rota, conteudo);
        return (Ano, numero);
    }

    public Task RegistrarRespostasAsync(IReadOnlyList<RegistroDeResposta> respostas, CancellationToken cancellationToken)
    {
        foreach (var r in respostas)
        {
            _respostas[r.Requisicao] = r;
        }

        return Task.CompletedTask;
    }

    public Chegada? ChegadaDe(long requisicao) => _chegadas.GetValueOrDefault(requisicao);

    /// <summary>Espera (até 5 s) a resposta gravada desta requisição: a gravação é assíncrona.</summary>
    public async Task<RegistroDeResposta> EsperarRespostaAsync(long requisicao)
    {
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            if (_respostas.TryGetValue(requisicao, out var resposta))
            {
                return resposta;
            }

            await Task.Delay(10, prazo.Token);
        }
    }

    /// <summary>Número da requisição a partir do header X-Requisicao (ex.: 2026-000123 vira 123).</summary>
    public static long NumeroDoHeader(HttpResponseMessage resposta)
    {
        ArgumentNullException.ThrowIfNull(resposta);
        var partes = resposta.Headers.GetValues(MovimentacaoMiddleware.Header).Single().Split('-');
        Assert.Equal(["2026", partes[1]], partes);
        return long.Parse(partes[1], CultureInfo.InvariantCulture);
    }
}

/// <summary>Destino falso do log de erros: guarda os registros.</summary>
public sealed class DestinoDeErrosFalso : IRepositorioDeErros
{
    private readonly ConcurrentQueue<RegistroDeErro> _registros = new();

    public IReadOnlyList<RegistroDeErro> Registros => [.. _registros];

    public Task GravarAsync(IReadOnlyList<RegistroDeErro> registros, CancellationToken cancellationToken)
    {
        foreach (var r in registros)
        {
            _registros.Enqueue(r);
        }

        return Task.CompletedTask;
    }

    /// <summary>Espera (até 5 s) chegar o registro com este request_id: a gravação é assíncrona.</summary>
    public async Task<RegistroDeErro> EsperarAsync(string requestId)
    {
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var achado = _registros.FirstOrDefault(r => r.RequestId == requestId);
            if (achado is not null)
            {
                return achado;
            }

            await Task.Delay(10, prazo.Token);
        }
    }
}
