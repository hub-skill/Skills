using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using ProjetoApi.Contracts;
using ProjetoApi.Errors;

namespace ProjetoApi.Endpoints.V1;

/// <summary>Rota com entrada JSON estruturada. Registre em Program.cs: <c>app.MapPedidoV1();</c></summary>
public static class PedidoEndpoints
{
    public static IEndpointRouteBuilder MapPedidoV1(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/v1")
            .RequireAuthorization()
            .WithTags("pedidos")
            .MapPost("/pedidos", CriarAsync)
            .WithName("CriarPedido")
            .Produces<SaidaPedido>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        return app;
    }

    private static async Task<Ok<SaidaPedido>> CriarAsync(
        EntradaPedido? entrada, IValidator<EntradaPedido> validador, CancellationToken cancellationToken)
    {
        // Corpo vazio ou "null": 422, como no Python (sem o "?" o ASP.NET respondia 400 antes de chegar aqui).
        if (entrada is null)
        {
            throw new EntradaInvalidaException("Dados de entrada inválidos.");
        }

        var validacao = await validador.ValidateAsync(entrada, cancellationToken);
        if (!validacao.IsValid)
        {
            // Mensagem genérica: não ecoa a entrada na resposta.
            throw new EntradaInvalidaException("Dados de entrada inválidos.");
        }

        // Cálculo curto e limitado (até 100 itens): roda direto. Se a regra for lenta, use o timeout como em
        // TransformacaoEndpoints. ADAPTE: a regra de negócio da API gerada.
        return TypedResults.Ok(new SaidaPedido(
            entrada.IdExterno,
            entrada.Itens.Count,
            entrada.Itens.Sum(i => (long)i.Quantidade),
            entrada.Itens.Sum(i => i.Quantidade * i.ValorUnitarioCentavos)));
    }
}
