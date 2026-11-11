using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjetoApi.Services;

namespace ProjetoApi.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ChaveDeTeste = "chave-de-teste-com-pelo-menos-32-caracteres";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Define a chave de teste sem depender de variáveis de ambiente da máquina.
        builder.UseSetting("Api:ApiKey", ChaveDeTeste);
    }
}

/// <summary>
/// Sobe a API com configurações próprias (rate limit baixo, docs ligadas, timeout curto) e,
/// se informado, troca o serviço por um falso (para provocar timeout ou erro inesperado).
/// </summary>
public sealed class ApiFactoryConfiguravel(
    IReadOnlyDictionary<string, string> configuracao, ITransformacaoService? servico = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Api:ApiKey", ApiFactory.ChaveDeTeste);
        foreach (var (chave, valor) in configuracao)
        {
            builder.UseSetting(chave, valor);
        }

        if (servico is not null)
        {
            builder.ConfigureTestServices(servicos =>
            {
                servicos.RemoveAll<ITransformacaoService>();
                servicos.AddSingleton(servico);
            });
        }
    }
}

/// <summary>Serviço falso: atrasa (para o timeout) ou lança uma exceção (para o erro 500).</summary>
public sealed class ServicoFalso(TimeSpan atraso = default, Exception? lancar = null) : ITransformacaoService
{
    public async Task<string> TransformarAsync(string texto, CancellationToken cancellationToken)
    {
        await Task.Delay(atraso, cancellationToken);
        if (lancar is not null)
        {
            throw lancar;
        }

        return texto;
    }
}
