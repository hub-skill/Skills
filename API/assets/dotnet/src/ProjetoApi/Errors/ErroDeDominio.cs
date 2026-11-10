namespace ProjetoApi.Errors;

public class ErroDeDominio(int statusCode, string codigo, string mensagem) : Exception(mensagem)
{
    public int StatusCode { get; } = statusCode;
    public string Codigo { get; } = codigo;
    public string Mensagem { get; } = mensagem;
}

public sealed class EntradaInvalidaException(string mensagem)
    : ErroDeDominio(StatusCodes.Status422UnprocessableEntity, "entrada_invalida", mensagem);

public sealed class ProcessamentoExpirouException()
    : ErroDeDominio(StatusCodes.Status504GatewayTimeout, "timeout", "O processamento excedeu o tempo limite.");
