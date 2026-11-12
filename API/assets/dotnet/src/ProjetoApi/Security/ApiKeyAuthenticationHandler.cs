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

    /// <summary>Valor do WWW-Authenticate no 401, igual ao do template Python.</summary>
    public const string DesafioWwwAuthenticate = "APIKey";

    /// <summary>Compara a chave recebida com a esperada em tempo constante (FixedTimeEquals; tamanhos diferentes = false).</summary>
    public static bool ChaveValida(string? recebida, string esperada)
    {
        ArgumentNullException.ThrowIfNull(esperada);
        return recebida is not null
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(recebida), Encoding.UTF8.GetBytes(esperada));
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var valores))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!ChaveValida(valores.ToString(), settings.ApiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Chave inválida"));
        }

        var identidade = new ClaimsIdentity(SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identidade), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    // 401 com o esquema esperado, como pede a especificação HTTP.
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = DesafioWwwAuthenticate;
        return base.HandleChallengeAsync(properties);
    }
}
