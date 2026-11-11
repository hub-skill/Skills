namespace ProjetoApi.Bancos;

/// <summary>Falha de banco já traduzida: a API responde 503 (indisponível) ou 504 (expirou), nunca 500.</summary>
public class ErroDeBancoException(string mensagem, Exception? causa) : Exception(mensagem, causa);

public sealed class BancoIndisponivelException(Exception? causa = null)
    : ErroDeBancoException("Banco de dados indisponível.", causa);

public sealed class BancoExpirouException(Exception? causa = null)
    : ErroDeBancoException("O banco de dados não respondeu dentro do tempo limite.", causa);
