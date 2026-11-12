"""O que a movimentação grava e o contrato do repositório (PostgreSQL e Oracle implementam o mesmo)."""

from dataclasses import dataclass
from typing import Protocol


@dataclass(frozen=True)
class RegistroErro:
    """Uma resposta de erro (4xx ou 5xx) para a log_erros. Só metadados: nunca documento, nome ou corpo.

    ``ano`` e ``requisicao`` apontam para a movimentação: sem eles o erro não é gravado (vai só para o log).
    """

    ano: int
    requisicao: int
    request_id: str
    metodo: str
    rota: str
    status: int
    codigo: str
    titulo: str
    tipo_excecao: str | None
    duracao_ms: int | None
    ambiente: str


@dataclass(frozen=True)
class RegistroResposta:
    """Resposta da API para completar a linha da movimentação (conteúdo já mascarado e limitado)."""

    ano: int
    requisicao: int
    status_http: int
    conteudo: str | None
    situacao_requisicao: str  # OK = aceita; ER = recusada (4xx)
    situacao_resposta: str  # OK = sucesso (2xx); ER = erro (4xx/5xx)


class RepositorioDeMovimentacao(Protocol):
    async def registrar_requisicao(self, metodo: str, rota: str, conteudo: str | None) -> tuple[int, int]:
        """Numera a requisição no ano e grava a chegada. Devolve (ano, requisicao)."""
        ...

    async def registrar_respostas(self, registros: list[RegistroResposta]) -> None:
        """Completa as linhas da movimentação com as respostas (cada linha uma vez só)."""
        ...

    async def gravar_erros(self, registros: list[RegistroErro]) -> None:
        """Grava um lote na tabela log_erros (a aplicação só tem INSERT nela)."""
        ...
