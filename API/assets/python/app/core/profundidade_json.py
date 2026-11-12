"""Corpo JSON que o template .NET não aceitaria na leitura: igualado aqui, antes do FastAPI ler o corpo.

Duas tolerâncias do parser do Python que o System.Text.Json (e a RFC 8259) não têm:
- **Aninhamento sem limite prático**: aceita milhares de níveis (1900 em 4 KB, medido). Como o FastAPI
  ignora campos desconhecidos, um JSON aninhado fundo num campo que a API nem usa passava com 200,
  enquanto o .NET recusa acima de 64 níveis (padrão do System.Text.Json).
- **NaN, Infinity e -Infinity**: não existem em JSON, mas o ``json`` do Python os aceita como número;
  aí o erro só aparecia na validação (422), e no .NET é erro de leitura (400).

Varre os bytes uma vez, sem montar o objeto. ``N`` e ``I`` maiúsculos fora de textos nunca aparecem em
JSON válido: os únicos valores com letras são ``true``, ``false`` e ``null``, em minúsculas.
"""

PROFUNDIDADE_MAXIMA = 64

_ASPAS, _BARRA = 0x22, 0x5C
_ABRE = (0x7B, 0x5B)  # { [
_FECHA = (0x7D, 0x5D)  # } ]
_FORA_DO_PADRAO = (0x4E, 0x49)  # N (NaN) e I (Infinity, -Infinity)


def corpo_json_ilegivel(corpo: bytes, maximo: int = PROFUNDIDADE_MAXIMA) -> bool:
    """True se objetos/listas passam de ``maximo`` níveis ou se há NaN/Infinity. Textos não contam."""
    nivel = 0
    em_texto = escapado = False
    for byte in corpo:
        if em_texto:
            if escapado:
                escapado = False
            elif byte == _BARRA:
                escapado = True
            elif byte == _ASPAS:
                em_texto = False
        elif byte == _ASPAS:
            em_texto = True
        elif byte in _ABRE:
            nivel += 1
            if nivel > maximo:
                return True
        elif byte in _FECHA:
            nivel -= 1
        elif byte in _FORA_DO_PADRAO:
            return True
    return False
