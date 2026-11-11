import pytest
from hypothesis import given
from hypothesis import strategies as st

from app.services.documentos import (
    cnpj_valido,
    cpf_valido,
    mascarar,
    miolo_cpf,
    normalizar_documento,
)

# Documentos válidos (dígitos verificadores corretos), usados só em testes.
CPF_VALIDO = "52998224725"
CNPJ_VALIDO = "00000000000191"


@pytest.mark.parametrize("entrada", ["529.982.247-25", "52998224725", " 529 982 247 25 "])
def test_cpf_aceita_formatos(entrada: str) -> None:
    assert normalizar_documento(entrada.strip()) == (CPF_VALIDO, "CPF")


@pytest.mark.parametrize("entrada", ["00.000.000/0001-91", "00000000000191", "19.182.685/0001-62"])
def test_cnpj_numerico_valido(entrada: str) -> None:
    doc, tipo = normalizar_documento(entrada)
    assert tipo == "CNPJ" and len(doc) == 14


def test_cnpj_alfanumerico_exemplo_da_receita() -> None:
    # Exemplo publicado pela Receita Federal para o CNPJ alfanumérico.
    assert normalizar_documento("12.ABC.345/01DE-35") == ("12ABC34501DE35", "CNPJ")


def test_cnpj_alfanumerico_minusculo_vira_maiusculo() -> None:
    assert normalizar_documento("12abc34501de35")[0] == "12ABC34501DE35"


@pytest.mark.parametrize(
    "entrada",
    [
        "52998224724",  # CPF com dígito errado
        "11111111111",  # CPF repetido
        "00000000000192",  # CNPJ com dígito errado
        "00000000000000",  # CNPJ repetido
        "1234567890",  # curto
        "529982247251",  # 12 dígitos
        "٥٢٩٩٨٢٢٤٧٢٥",  # dígitos arábicos
        "12ABC34501DE3A",  # letra no dígito verificador
    ],
)
def test_documentos_invalidos(entrada: str) -> None:
    with pytest.raises(ValueError):
        normalizar_documento(entrada)


def test_mascaras() -> None:
    assert mascarar(CPF_VALIDO, "CPF") == "***.982.247-**"
    assert mascarar(CNPJ_VALIDO, "CNPJ") == "00.000.000/0001-91"
    assert miolo_cpf(CPF_VALIDO) == "982247"


@given(st.text(max_size=30))
def test_validacao_nunca_quebra_com_texto_qualquer(texto: str) -> None:
    try:
        doc, tipo = normalizar_documento(texto)
    except ValueError:
        return
    assert cpf_valido(doc) if tipo == "CPF" else cnpj_valido(doc)
