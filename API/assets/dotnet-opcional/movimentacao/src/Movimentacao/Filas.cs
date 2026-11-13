namespace ProjetoApi.Movimentacao;

/// <summary>Fila da movimentação (e do erro de cada requisição). Fila e lotes: <see cref="FilaDeGravacao{T}"/>.</summary>
public sealed class FilaDeMovimentacao(ILogger<FilaDeMovimentacao> logger, int capacidade = FilaDeMovimentacao.Capacidade)
    : FilaDeGravacao<RegistroDeMovimentacao>(logger, "da movimentação", capacidade);

/// <summary>Grava a fila na tabela movimentacao (e log_erros), em segundo plano.</summary>
public sealed class GravadorDeMovimentacao(
    FilaDeMovimentacao fila, IRepositorioDeMovimentacao destino, ILogger<GravadorDeMovimentacao> logger)
    : GravadorEmLotes<RegistroDeMovimentacao>(fila, destino.GravarAsync, logger, "a movimentação");
