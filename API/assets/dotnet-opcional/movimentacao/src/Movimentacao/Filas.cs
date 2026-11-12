namespace ProjetoApi.Movimentacao;

/// <summary>Fila do log de erros (tabela log_erros). Fila e lotes: <see cref="FilaDeGravacao{T}"/>.</summary>
public sealed class FilaDeErros(ILogger<FilaDeErros> logger, int capacidade = FilaDeErros.Capacidade)
    : FilaDeGravacao<RegistroDeErro>(logger, "do log de erros", capacidade);

/// <summary>Grava a fila de erros na tabela log_erros.</summary>
public sealed class GravadorDeErros(FilaDeErros fila, IRepositorioDeErros destino, ILogger<GravadorDeErros> logger)
    : GravadorEmLotes<RegistroDeErro>(fila, destino.GravarAsync, logger, "o log de erros");

/// <summary>Fila das respostas que completam a movimentação.</summary>
public sealed class FilaDeRespostas(ILogger<FilaDeRespostas> logger, int capacidade = FilaDeRespostas.Capacidade)
    : FilaDeGravacao<RegistroDeResposta>(logger, "da movimentação", capacidade);

/// <summary>Completa a movimentação com as respostas da fila, em segundo plano.</summary>
public sealed class GravadorDeRespostas(
    FilaDeRespostas fila, IRepositorioDeMovimentacao destino, ILogger<GravadorDeRespostas> logger)
    : GravadorEmLotes<RegistroDeResposta>(fila, destino.RegistrarRespostasAsync, logger, "a resposta na movimentação");
