using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using ProjetoApi.Configuration;

namespace ProjetoApi.Security;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiSettings settings)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string Header = "X-API-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var valores))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // FixedTimeEquals compara em tempo constante e retorna false se os tamanhos diferirem.
        var recebida = Encoding.UTF8.GetBytes(valores.ToString());
        var esperada = Encoding.UTF8.GetBytes(settings.ApiKey);
        if (!CryptographicOperations.FixedTimeEquals(recebida, esperada))
        {
            return Task.FromResult(AuthenticateResult.Fail("Chave inválida"));
        }

        var identidade = new ClaimsIdentity(SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identidade), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
