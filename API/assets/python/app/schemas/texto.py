import re

from pydantic import BaseModel, Field, field_validator

# Caracteres de controle (exceto tab, LF e CR) e bytes nulos.
CARACTERES_DE_CONTROLE = re.compile(r"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]")
TAMANHO_MAXIMO_TEXTO = 10_000


class EntradaTexto(BaseModel):
    texto: str = Field(
        ...,
        min_length=1,
        max_length=TAMANHO_MAXIMO_TEXTO,
        description="Texto a ser processado",
        examples=["  exemplo de texto  "],
    )

    @field_validator("texto")
    @classmethod
    def validar_texto(cls, valor: str) -> str:
        valor = valor.strip()
        if not valor:
            raise ValueError("texto não pode ser vazio após remover espaços")
        if CARACTERES_DE_CONTROLE.search(valor):
            raise ValueError("texto contém caracteres de controle ou bytes nulos")
        return valor


class SaidaTexto(BaseModel):
    resultado: str = Field(..., description="Texto processado", examples=["EXEMPLO"])
