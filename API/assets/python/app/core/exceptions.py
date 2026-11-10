class ErroDeDominio(Exception):
    """Base para erros de regra de negócio. Cada subclasse define status e código."""

    status_code: int = 400
    codigo: str = "erro_de_dominio"
    mensagem: str = "Requisição inválida."


class EntradaInvalida(ErroDeDominio):
    status_code = 422
    codigo = "entrada_invalida"
    mensagem = "Dados de entrada inválidos."


class ProcessamentoExpirou(ErroDeDominio):
    status_code = 504
    codigo = "timeout"
    mensagem = "O processamento excedeu o tempo limite."
