namespace ProjetoApi.Movimentacao;

/// <summary>
/// Uma resposta de erro (4xx ou 5xx) da API, para a tabela log_erros. Só metadados: nunca documento, nome ou corpo da
/// requisição (o corpo, mascarado, fica na movimentação). Mesmos campos nos templates Python e .NET. A ligação com a
/// movimentação é o <c>requisicao_id</c> (ULID): o erro é gravado junto com a sua movimentação.
/// </summary>
public sealed record RegistroDeErro(
    string RequestId,
    string Metodo,
    string Rota,
    int Status,
    string Codigo,
    string Titulo,
    string? TipoExcecao,
    int? DuracaoMs,
    string Ambiente);
