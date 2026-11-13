"""O que a movimentação grava e o contrato do repositório (PostgreSQL e Oracle implementam o mesmo)."""

from dataclasses import dataclass
from datetime import datetime
from typing import Protocol


@dataclass(frozen=True)
class RegistroErro:
    """Uma resposta de erro (4xx ou 5xx) para a log_erros. Só metadados: nunca documento, nome ou corpo.

    A ligação com a movimentação é o ``requisicao_id`` (ULID): o erro é gravado junto com a sua movimentação.
    """

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
class RegistroMovimentacao:
    """Uma requisição com chave de API válida: o que chegou, o que a API respondeu e o erro, se houve.

    Gravada de uma vez só, em segundo plano, depois da resposta (a chegada não depende do banco). Os conteúdos
    já vêm mascarados e limitados; ``requisicao_em`` e ``resposta_em`` têm fuso (UTC).
    """

    requisicao_id: str  # ULID: o mesmo que vai ao cliente no header X-Requisicao
    metodo: str
    rota: str
    requisicao_conteudo: str | None
    requisicao_em: datetime
    status_http: int
    resposta_conteudo: str | None
    resposta_em: datetime
    situacao_requisicao: str  # OK = aceita; ER = recusada (4xx)
    situacao_resposta: str  # OK = sucesso (2xx); ER = erro (4xx/5xx)
    erro: RegistroErro | None = None


class RepositorioDeMovimentacao(Protocol):
    async def gravar(self, registros: list[RegistroMovimentacao]) -> None:
        """Grava o lote: cada movimentação e, quando houver, o seu erro, numa única transação."""
        ...
