"""Entrada em JSON estruturado (exemplo: pedido com cliente e itens). ADAPTE os campos à API gerada.

Proteções que valem para qualquer entrada JSON:
- ``extra="forbid"``: campo desconhecido é rejeitado (422), em qualquer nível. Sem isso, um erro de
  digitação do cliente ("quantidad") passa calado e o valor some.
- ``strict=True``: sem conversão de tipos ("2" não vira 2, true não vira 1, 1.5 não vira 1).
- Limites em todo campo: tamanho de texto, faixa de número e quantidade de itens da lista.
- Dinheiro em centavos (inteiro): nunca float.
- Texto sem caracteres de controle, com espaços das pontas removidos.
"""

import re
from typing import Annotated

from pydantic import AfterValidator, BaseModel, ConfigDict, Field

_CONTROLE = re.compile(r"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]")
_EMAIL = re.compile(r"^[^@\s]+@[^@\s]+\.[^@\s]+$")

MAXIMO_DE_ITENS = 100


def _texto_limpo(valor: str) -> str:
    valor = valor.strip()
    if not valor:
        raise ValueError("texto vazio")
    if _CONTROLE.search(valor):
        raise ValueError("texto com caracteres de controle")
    return valor


def _email(valor: str) -> str:
    if not _EMAIL.match(valor):
        raise ValueError("e-mail inválido")
    return valor


Texto = Annotated[str, AfterValidator(_texto_limpo)]
Email = Annotated[str, Field(max_length=254), AfterValidator(_email)]


class ModeloDeEntrada(BaseModel):
    """Base de toda entrada JSON: sem campos desconhecidos e sem conversão de tipos."""

    model_config = ConfigDict(extra="forbid", strict=True)


class Cliente(ModeloDeEntrada):
    nome: Annotated[Texto, Field(max_length=200)]
    email: Email | None = None


class Item(ModeloDeEntrada):
    descricao: Annotated[Texto, Field(max_length=200)]
    quantidade: Annotated[int, Field(ge=1, le=10_000)]
    valor_unitario_centavos: Annotated[int, Field(ge=0, le=100_000_000)]


class EntradaPedido(ModeloDeEntrada):
    id_externo: Annotated[str, Field(min_length=1, max_length=64, pattern=r"^[A-Za-z0-9._-]+$")]
    cliente: Cliente
    itens: Annotated[list[Item], Field(min_length=1, max_length=MAXIMO_DE_ITENS)]
    observacao: Annotated[Texto | None, Field(max_length=1000)] = None


class SaidaPedido(BaseModel):
    """Devolve só o calculado: nada de nome ou e-mail do cliente (dados pessoais não voltam na resposta)."""

    id_externo: str
    quantidade_itens: int
    quantidade_total: int
    valor_total_centavos: int
