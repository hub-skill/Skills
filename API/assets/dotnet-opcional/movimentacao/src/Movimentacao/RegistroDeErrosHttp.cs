using System.Diagnostics;

namespace ProjetoApi.Movimentacao;

/// <summary>
/// Liga o <c>RespostaDeErro</c> do template ao log de erros: toda resposta de erro (4xx e 5xx) vai para a fila da
/// tabela log_erros, com a chave (ano, requisicao) da movimentação. Só metadados: nunca o corpo da requisição.
/// Sem chave (rota sem movimentação, chamada sem chave de API válida ou banco fora na chegada), o erro não é
/// gravado: a log_erros nunca tem linha sem (ano, requisicao).
/// </summary>
public static class RegistroDeErrosHttp
{
    /// <summary>Chave em HttpContext.Items com o instante de início da requisição (Stopwatch), para a duração.</summary>
    public const string ChaveDoInicio = "movimentacao.inicio";

    /// <summary>Chave em HttpContext.Items com (ano, requisicao) da movimentação desta requisição, se foi numerada.</summary>
    public const string ChaveDaMovimentacao = "movimentacao.chave";

    public static void Registrar(HttpContext contexto, int status, string codigo, string titulo, string? tipoExcecao)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        // Com a resposta guardada em memória (MovimentacaoMiddleware), descarta o que um erro tenha deixado escrito.
        if (!contexto.Response.HasStarted && contexto.Response.Body is MemoryStream { CanSeek: true } guardada)
        {
            guardada.SetLength(0);
        }

        var fila = contexto.RequestServices?.GetService<FilaDeErros>();
        if (fila is null
            || !contexto.Items.TryGetValue(ChaveDaMovimentacao, out var numero)
            || numero is not ValueTuple<int, long> chave)
        {
            return;
        }

        int? duracao = contexto.Items.TryGetValue(ChaveDoInicio, out var inicio) && inicio is long marca
            ? (int)Math.Min(int.MaxValue, Stopwatch.GetElapsedTime(marca).TotalMilliseconds)
            : null;
        var ambiente = contexto.RequestServices!.GetService<IHostEnvironment>()?.EnvironmentName ?? "-";
        fila.Registrar(new RegistroDeErro(
            chave.Item1,
            chave.Item2,
            Cortar(contexto.TraceIdentifier, 64),
            Cortar(contexto.Request.Method, 10),
            Cortar(contexto.Request.Path.Value is { Length: > 0 } caminho ? caminho : "/", 200),
            status,
            Cortar(codigo, 40),
            Cortar(titulo, 200),
            tipoExcecao is null ? null : Cortar(tipoExcecao, 200),
            duracao,
            Cortar(ambiente, 20)));
    }

    private static string Cortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}
