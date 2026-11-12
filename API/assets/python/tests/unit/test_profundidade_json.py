from app.core.profundidade_json import corpo_json_ilegivel


def test_conta_objetos_e_listas_aninhados() -> None:
    assert not corpo_json_ilegivel(b'{"a": [1, {"b": 2}]}', maximo=3)
    assert corpo_json_ilegivel(b'{"a": [1, {"b": [2]}]}', maximo=3)


def test_ignora_chaves_colchetes_e_letras_dentro_de_textos_inclusive_com_aspas_escapadas() -> None:
    assert not corpo_json_ilegivel(b'{"a": "[[[[{{{{ NaN Infinity", "b": "\\"[[[["}', maximo=1)


def test_nan_e_infinity_fora_de_texto_sao_recusados() -> None:
    for corpo in (b'{"a": NaN}', b'{"a": Infinity}', b'{"a": -Infinity}', b"[1, NaN]"):
        assert corpo_json_ilegivel(corpo)


def test_valores_validos_com_letras_e_expoente_passam() -> None:
    assert not corpo_json_ilegivel(b'{"a": true, "b": false, "c": null, "d": -1.5e10, "e": 2E-3}')


def test_corpo_vazio_nao_e_recusado_aqui() -> None:
    # Corpo vazio fica para a validação (422), como no template .NET.
    assert not corpo_json_ilegivel(b"")
