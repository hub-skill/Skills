using System.Threading.Channels;

namespace ProjetoApi.Movimentacao;

/// <summary>
/// Fila em memória de gravação (usada pelo log de erros e pela resposta da movimentação). A resposta ao cliente
/// nunca espera o banco: o registro entra aqui e um <see cref="GravadorEmLotes{T}"/> grava em lotes. Com a fila
/// cheia (banco lento ou fora), o registro é descartado e contado. Mesmo comportamento do template Python
/// (app/movimentacao/gravacao.py).
/// </summary>
public class FilaDeGravacao<T>
{
    public const int Capacidade = 1000;

    private static readonly Action<ILogger, string, long, Exception?> LogFilaCheia = LoggerMessage.Define<string, long>(
        LogLevel.Warning, new EventId(1, "FilaCheia"), "fila {Fila} cheia: {Total} registros descartados");

    private readonly ILogger _logger;
    private readonly string _nome;
    private long _descartados;

    protected FilaDeGravacao(ILogger logger, string nome, int capacidade)
    {
        _logger = logger;
        _nome = nome;
        Canal = Channel.CreateBounded<T>(
            new BoundedChannelOptions(capacidade) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true },
            _ =>
            {
                var total = Interlocked.Increment(ref _descartados);
                if (total == 1 || total % 100 == 0)
                {
                    LogFilaCheia(_logger, _nome, total, null);
                }
            });
    }

    internal Channel<T> Canal { get; }

    public long Descartados => Interlocked.Read(ref _descartados);

    /// <summary>Não bloqueia: com a fila cheia, descarta (o cliente já recebeu a resposta).</summary>
    public void Registrar(T registro) => Canal.Writer.TryWrite(registro);
}

/// <summary>Tarefa de fundo que grava a fila em lotes. Uma falha do banco nunca derruba a API.</summary>
public abstract class GravadorEmLotes<T>(
    FilaDeGravacao<T> fila,
    Func<IReadOnlyList<T>, CancellationToken, Task> gravar,
    ILogger logger,
    string alvo) : BackgroundService
{
    public const int TamanhoDoLote = 100;

    private static readonly Action<ILogger, string, string, int, Exception?> LogFalha =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Warning,
            new EventId(2, "FalhaAoGravar"),
            "falha ao gravar {Alvo} no banco: {Tipo} ({Registros} registros)");

    private static readonly TimeSpan PrazoAoParar = TimeSpan.FromSeconds(5);
    private long _perdidos;

    /// <summary>Registros de lotes que o banco recusou.</summary>
    public long Perdidos => Interlocked.Read(ref _perdidos);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // Grava o que ainda está na fila antes de a aplicação fechar.
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        prazo.CancelAfter(PrazoAoParar);
        while (!prazo.IsCancellationRequested && fila.Canal.Reader.TryPeek(out _))
        {
            await GravarAsync(RetirarLote(), prazo.Token).ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await fila.Canal.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                await GravarAsync(RetirarLote(), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Parada normal: o StopAsync grava o que sobrou.
        }
    }

    private List<T> RetirarLote()
    {
        var lote = new List<T>(TamanhoDoLote);
        while (lote.Count < TamanhoDoLote && fila.Canal.Reader.TryRead(out var registro))
        {
            lote.Add(registro);
        }

        return lote;
    }

    private async Task GravarAsync(List<T> lote, CancellationToken cancellationToken)
    {
        if (lote.Count == 0)
        {
            return;
        }

        try
        {
            await gravar(lote, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // a gravação em segundo plano nunca pode derrubar a API: qualquer falha do banco só é registrada
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            Interlocked.Add(ref _perdidos, lote.Count);
            // Só o tipo: a mensagem do driver pode trazer detalhes da conexão.
            LogFalha(logger, alvo, e.GetType().Name, lote.Count, null);
        }
    }
}
