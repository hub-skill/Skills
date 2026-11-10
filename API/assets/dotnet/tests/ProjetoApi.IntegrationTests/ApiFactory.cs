using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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
