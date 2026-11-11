namespace ProjetoApi.Errors;

public class ErroDeDominioException(int statusCode, string codigo, string mensagem) : Exception(mensagem)
{
    public int StatusCode { get; } = statusCode;
    public string Codigo { get; } = codigo;
    public string Mensagem { get; } = mensagem;
}

public sealed class EntradaInvalidaException(string mensagem)
    : ErroDeDominioException(StatusCodes.Status422UnprocessableEntity, "entrada_invalida", mensagem);

public sealed class CorpoMuitoGrandeException()
    : ErroDeDominioException(
        StatusCodes.Status413PayloadTooLarge, "corpo_muito_grande", "Corpo da requisição excede o limite.");

public sealed class ProcessamentoExpirouException()
    : ErroDeDominioException(StatusCodes.Status504GatewayTimeout, "timeout", "O processamento excedeu o tempo limite.");
