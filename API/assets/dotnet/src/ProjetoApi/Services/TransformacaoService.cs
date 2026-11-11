namespace ProjetoApi.Services;

public interface ITransformacaoService
{
    Task<string> TransformarAsync(string texto, CancellationToken cancellationToken);
}

public sealed class TransformacaoService : ITransformacaoService
{
    private readonly Func<string, string> _transformar;

    // Usado pela injeção de dependência: a regra real.
    public TransformacaoService()
        : this(Transformar)
    {
    }

    // Permite testar o serviço real com uma regra lenta (timeout) sem mudar a regra de negócio.
    public TransformacaoService(Func<string, string> transformar) => _transformar = transformar;

    // Trabalho CPU-bound fora da thread da requisição.
    // O token de Task.Run só evita INICIAR o trabalho; WaitAsync faz a espera terminar quando o tempo acaba
    // (sem ele, o 504 só sairia depois que o trabalho terminasse). Como em Python, o trabalho em si não é
    // interrompido: segue em segundo plano e o resultado é descartado.
    public Task<string> TransformarAsync(string texto, CancellationToken cancellationToken) =>
        Task.Run(() => _transformar(texto), cancellationToken).WaitAsync(cancellationToken);

    // Regra de negócio. Substitua pela transformação real. Mantenha-a pura.
    public static string Transformar(string texto) => texto.ToUpperInvariant();
}
