namespace ProjetoApi.Movimentacao;

/// <summary>
/// Uma resposta de erro (4xx ou 5xx) da API, para a tabela log_erros. Só metadados: nunca documento, nome ou
/// corpo da requisição (o corpo, mascarado, fica na movimentação). Mesmos campos nos templates Python e .NET.
/// <para>
/// <see cref="Ano"/> e <see cref="Requisicao"/> apontam para a movimentação do erro: sem eles o erro não é
/// gravado (vai só para o log da aplicação).
/// </para>
/// </summary>
public sealed record RegistroDeErro(
    int Ano,
    long Requisicao,
    string RequestId,
    string Metodo,
    string Rota,
    int Status,
    string Codigo,
    string Titulo,
    string? TipoExcecao,
    int? DuracaoMs,
    string Ambiente);

/// <summary>Grava lotes na tabela log_erros. A aplicação só tem INSERT nela.</summary>
public interface IRepositorioDeErros
{
    Task GravarAsync(IReadOnlyList<RegistroDeErro> registros, CancellationToken cancellationToken);
}
