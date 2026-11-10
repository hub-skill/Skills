using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using ProjetoApi.Configuration;
using ProjetoApi.Contracts;
using ProjetoApi.Errors;
using ProjetoApi.Services;

namespace ProjetoApi.Endpoints.V1;

public static class TransformacaoEndpoints
{
    public static IEndpointRouteBuilder MapTransformacaoV1(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/v1")
            .RequireAuthorization()
            .WithTags("transformacao");

        grupo.MapPost("/transformar", TransformarAsync)
            .WithName("TransformarTexto")
            .Produces<SaidaTexto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return app;
    }

    private static async Task<Ok<SaidaTexto>> TransformarAsync(
        EntradaTexto entrada,
        IValidator<EntradaTexto> validador,
        ITransformacaoService servico,
        ApiSettings settings,
        CancellationToken cancellationToken)
    {
        // Remove espaços nas extremidades antes de validar, como no template Python.
        var normalizada = new EntradaTexto(entrada.Texto.Trim());

        var validacao = await validador.ValidateAsync(normalizada, cancellationToken);
        if (!validacao.IsValid)
        {
            // Mensagem genérica: não ecoa a entrada na resposta.
            throw new EntradaInvalidaException("Dados de entrada inválidos.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));

        try
        {
            var resultado = await servico.TransformarAsync(normalizada.Texto, cts.Token);
            return TypedResults.Ok(new SaidaTexto(resultado));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Expirou o nosso timeout (não foi o cliente que cancelou).
            throw new ProcessamentoExpirouException();
        }
    }
}
