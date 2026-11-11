"""Validação de CPF e CNPJ (inclusive o CNPJ alfanumérico)."""

import re
from typing import Literal

_PONTUACAO = re.compile(r"[.\-/\s]")
_CPF = re.compile(r"[0-9]{11}")  # [0-9], não \d: \d aceitaria dígitos Unicode como "٣"
# CNPJ alfanumérico (Receita Federal, a partir de 2026): 12 posições [0-9A-Z] + 2 dígitos.
_CNPJ = re.compile(r"[0-9A-Z]{12}[0-9]{2}")

TipoDocumento = Literal["CPF", "CNPJ"]


def normalizar_documento(valor: str) -> tuple[str, TipoDocumento]:
    """Remove pontuação, valida e devolve (documento, tipo). Lança ValueError se inválido."""
    doc = _PONTUACAO.sub("", valor).upper()
    if _CPF.fullmatch(doc):
        if not cpf_valido(doc):
            raise ValueError("CPF inválido")
        return doc, "CPF"
    if _CNPJ.fullmatch(doc):
        if not cnpj_valido(doc):
            raise ValueError("CNPJ inválido")
        return doc, "CNPJ"
    raise ValueError("Documento deve ser um CPF (11 dígitos) ou CNPJ (14 posições)")


def cpf_valido(digitos: str) -> bool:
    if not _CPF.fullmatch(digitos) or digitos == digitos[0] * 11:
        return False
    numeros = [int(d) for d in digitos]
    for posicao in (9, 10):
        soma = sum(numeros[i] * (posicao + 1 - i) for i in range(posicao))
        if (soma * 10) % 11 % 10 != numeros[posicao]:
            return False
    return True


def cnpj_valido(valor: str) -> bool:
    """Módulo 11 com pesos 2..9; cada caractere vale ord(c) - 48 (regra do CNPJ alfanumérico)."""
    if not _CNPJ.fullmatch(valor) or valor == valor[0] * 14:
        return False
    valores = [ord(c) - 48 for c in valor]
    for tamanho in (12, 13):
        pesos = [(i % 8) + 2 for i in range(tamanho)][::-1]
        resto = sum(v * p for v, p in zip(valores[:tamanho], pesos, strict=True)) % 11
        digito = 0 if resto < 2 else 11 - resto
        if digito != valores[tamanho]:
            return False
    return True


def mascarar(documento: str, tipo: TipoDocumento) -> str:
    """CPF '12345678909' -> '***.456.789-**' (mesmo formato da CGU). CNPJ é público: só formata."""
    if tipo == "CPF":
        return f"***.{documento[3:6]}.{documento[6:9]}-**"
    d = documento
    return f"{d[:2]}.{d[2:5]}.{d[5:8]}/{d[8:12]}-{d[12:]}"


def miolo_cpf(cpf: str) -> str:
    """Os 6 dígitos que a CGU deixa visíveis no CPF mascarado (posições 4 a 9)."""
    return cpf[3:9]
