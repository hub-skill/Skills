using System.Diagnostics;

namespace ProjetoApi.Movimentacao;

/// <summary>
/// Liga o <c>RespostaDeErro</c> do template à movimentação: toda resposta de erro (4xx e 5xx) deixa os seus metadados no
/// <c>HttpContext.Items</c>, e o <see cref="MovimentacaoMiddleware"/> os grava junto com a movimentação da requisição. Só
/// metadados: nunca o corpo da requisição. Requisição sem movimentação (rota excluída ou sem chave de API válida) não tem erro
/// gravado: fica só no log da aplicação.
/// </summary>
public static class RegistroDeErrosHttp
{
    /// <summary>Chave em HttpContext.Items com o instante de início da requisição (Stopwatch), para a duração.</summary>
    public const string ChaveDoInicio = "movimentacao.inicio";

    /// <summary>Chave em HttpContext.Items com o ULID desta requisição, se ela entra na movimentação.</summary>
    public const string ChaveDaMovimentacao = "movimentacao.id";

    /// <summary>Chave em HttpContext.Items com o <see cref="RegistroDeErro"/> da resposta de erro.</summary>
    public const string ChaveDoErro = "movimentacao.erro";

    public static void Registrar(HttpContext contexto, int status, string codigo, string titulo, string? tipoExcecao)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        // Com a resposta guardada em memória (MovimentacaoMiddleware), descarta o que um erro tenha deixado escrito.
        if (!contexto.Response.HasStarted && contexto.Response.Body is MemoryStream { CanSeek: true } guardada)
        {
            guardada.SetLength(0);
        }

        if (!contexto.Items.ContainsKey(ChaveDaMovimentacao))
        {
            return;
        }

        int? duracao = contexto.Items.TryGetValue(ChaveDoInicio, out var inicio) && inicio is long marca
            ? (int)Math.Min(int.MaxValue, Stopwatch.GetElapsedTime(marca).TotalMilliseconds)
            : null;
        var ambiente = contexto.RequestServices?.GetService<IHostEnvironment>()?.EnvironmentName ?? "-";
        contexto.Items[ChaveDoErro] = new RegistroDeErro(
            Cortar(contexto.TraceIdentifier, 64),
            Cortar(contexto.Request.Method, 10),
            Cortar(contexto.Request.Path.Value is { Length: > 0 } caminho ? caminho : "/", 200),
            status,
            Cortar(codigo, 40),
            Cortar(titulo, 200),
            tipoExcecao is null ? null : Cortar(tipoExcecao, 200),
            duracao,
            Cortar(ambiente, 20));
    }

    private static string Cortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}
