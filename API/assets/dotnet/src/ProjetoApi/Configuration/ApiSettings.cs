using System.ComponentModel.DataAnnotations;

namespace ProjetoApi.Configuration;

public sealed class ApiSettings
{
    public const string SectionName = "Api";

    // Vazio ou curta demais: a aplicação não sobe (ValidateOnStart).
    // Defina via variável de ambiente: Api__ApiKey
    [Required, MinLength(32)]
    public string ApiKey { get; init; } = string.Empty;

    public string[] AllowedOrigins { get; init; } = [];

    public string[] AllowedHosts { get; init; } = ["localhost"];

    [Range(1, 60)]
    public double RequestTimeoutSeconds { get; init; } = 5;

    // Requisições por minuto, por IP. Configurável (Api__RateLimitPorMinuto) e testável.
    [Range(1, 100_000)]
    public int RateLimitPorMinuto { get; init; } = 60;

    public bool EnableDocs { get; init; }
}
