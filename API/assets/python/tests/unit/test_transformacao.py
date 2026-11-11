"""Testes unitários da regra (função pura) e do schema. Troque pela regra real da API."""

import pytest
from hypothesis import given
from hypothesis import strategies as st
from pydantic import ValidationError

from app.schemas.texto import EntradaTexto
from app.services.transformacao import get_transformador, transformar


@pytest.mark.parametrize(("entrada", "esperado"), [("ola", "OLA"), ("ção", "ÇÃO")])
def test_transforma_para_maiusculas(entrada: str, esperado: str) -> None:
    assert transformar(entrada) == esperado


def test_dependencia_devolve_a_regra() -> None:
    assert get_transformador() is transformar


@pytest.mark.parametrize("texto", ["ola", "  ola  ", "linha1\nlinha2\tcom tab"])
def test_schema_aceita_e_remove_espacos(texto: str) -> None:
    assert EntradaTexto(texto=texto).texto == texto.strip()


@pytest.mark.parametrize("texto", ["", "   ", "a" * 10_001, "ola\x00", "ola\x07"])
def test_schema_recusa(texto: str) -> None:
    with pytest.raises(ValidationError):
        EntradaTexto(texto=texto)


@given(st.text(alphabet=st.characters(max_codepoint=127), max_size=200))
def test_transformacao_preserva_tamanho_de_ascii(texto: str) -> None:
    assert len(transformar(texto)) == len(texto)
