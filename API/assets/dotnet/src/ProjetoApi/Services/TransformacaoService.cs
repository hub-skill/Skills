namespace ProjetoApi.Services;

public interface ITransformacaoService
{
    Task<string> TransformarAsync(string texto, CancellationToken cancellationToken);
}

public sealed class TransformacaoService : ITransformacaoService
{
    // Trabalho CPU-bound fora da thread da requisição.
    // Obs.: como em Python, o trabalho não é interrompido no timeout; só o cliente recebe 504.
    public Task<string> TransformarAsync(string texto, CancellationToken cancellationToken) =>
        Task.Run(() => Transformar(texto), cancellationToken);

    // Regra de negócio. Substitua pela transformação real. Mantenha-a pura.
    public static string Transformar(string texto) => texto.ToUpperInvariant();
}
