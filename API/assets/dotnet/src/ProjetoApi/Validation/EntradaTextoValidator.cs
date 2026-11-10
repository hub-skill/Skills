using System.Text.RegularExpressions;
using FluentValidation;
using ProjetoApi.Contracts;

namespace ProjetoApi.Validation;

public sealed partial class EntradaTextoValidator : AbstractValidator<EntradaTexto>
{
    private const int TamanhoMaximo = 10_000;

    public EntradaTextoValidator()
    {
        // O texto já chega sem espaços nas extremidades (ver TransformacaoEndpoints).
        RuleFor(x => x.Texto)
            .NotEmpty()
            .MaximumLength(TamanhoMaximo)
            .Must(NaoTemCaracteresDeControle)
            .WithMessage("texto contém caracteres de controle ou bytes nulos");
    }

    private static bool NaoTemCaracteresDeControle(string texto) =>
        !CaracteresDeControle().IsMatch(texto);

    // Tab, LF e CR são permitidos; os demais caracteres de controle e o byte nulo não.
    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]")]
    private static partial Regex CaracteresDeControle();
}
