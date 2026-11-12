using System.Text.RegularExpressions;
using FluentValidation;
using ProjetoApi.Contracts;

namespace ProjetoApi.Validation;

/// <summary>
/// Limites de todo campo da entrada JSON (os mesmos do Python). Tipos, campos desconhecidos, campos obrigatórios e
/// null já são barrados antes, na leitura do JSON (ver LEIAME): aqui ficam tamanhos, faixas e formatos.
/// </summary>
public sealed partial class EntradaPedidoValidator : AbstractValidator<EntradaPedido>
{
    public const int MaximoDeItens = 100;

    public EntradaPedidoValidator()
    {
        RuleFor(x => x.IdExterno).NotEmpty().MaximumLength(64).Matches(IdPermitido());
        RuleFor(x => x.Cliente).NotNull().SetValidator(new ClienteValidator());
        RuleFor(x => x.Itens).NotNull().Must(itens => itens.Count is >= 1 and <= MaximoDeItens);
        RuleForEach(x => x.Itens).NotNull().SetValidator(new ItemValidator());
        RuleFor(x => x.Observacao).Must(o => o is null || TextoValido(o, 1000));
    }

    /// <summary>Sem espaços nas pontas não pode ficar vazio, cabe no limite e não tem caracteres de controle.</summary>
    internal static bool TextoValido(string? texto, int maximo)
    {
        var limpo = texto?.Trim();
        return !string.IsNullOrEmpty(limpo) && limpo.Length <= maximo && !CaracteresDeControle().IsMatch(limpo);
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex IdPermitido();

    // Tab, LF e CR são permitidos; os demais caracteres de controle e o byte nulo não.
    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]")]
    internal static partial Regex CaracteresDeControle();
}

public sealed partial class ClienteValidator : AbstractValidator<ClienteDoPedido>
{
    public ClienteValidator()
    {
        RuleFor(x => x.Nome).Must(n => EntradaPedidoValidator.TextoValido(n, 200));
        RuleFor(x => x.Email).Must(e => e is null || (e.Length <= 254 && Email().IsMatch(e)));
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex Email();
}

public sealed class ItemValidator : AbstractValidator<ItemDoPedido>
{
    public ItemValidator()
    {
        RuleFor(x => x.Descricao).Must(d => EntradaPedidoValidator.TextoValido(d, 200));
        RuleFor(x => x.Quantidade).InclusiveBetween(1, 10_000);
        RuleFor(x => x.ValorUnitarioCentavos).InclusiveBetween(0, 100_000_000);
    }
}
