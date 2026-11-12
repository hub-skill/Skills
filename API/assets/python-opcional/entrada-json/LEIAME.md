# Módulo opcional: entrada JSON (Python)

Use quando a API **recebe um JSON estruturado** (vários campos, objetos aninhados, listas), em vez de um único texto.
É a opção **padrão** quando o usuário não escolhe entre String e JSON (ver SKILL.md, Passo 3).

O exemplo é um pedido com cliente e itens (`POST /api/v1/pedidos`). **ADAPTE** os campos, os limites e a regra de
negócio; mantenha as proteções.

## Arquivos

| Arquivo | O que faz |
|---|---|
| `app/schemas/pedido.py` | `ModeloDeEntrada` (base: `extra="forbid"`, `strict=True`), `Cliente`, `Item`, `EntradaPedido`, `SaidaPedido` |
| `app/services/pedido.py` | Regra de negócio do exemplo (`totalizar`) |
| `app/api/v1/pedido.py` | Rota `POST /api/v1/pedidos` com chave de API e rate limit |
| `tests/integration/test_entrada_json.py` | 20 testes: válido, opcionais, 15 formas erradas (422), lista no topo, JSON fundo demais (400), sem eco |

## Passo a passo

1. Copie as pastas `app/` e `tests/` por cima do projeto gerado.
2. Em `app/main.py`, registre a rota:

```python
from app.api.v1.pedido import router_pedido
...
app.include_router(router_pedido)
```

3. Se a API não tiver também a rota de texto, apague `app/schemas/texto.py`, `app/services/transformacao.py`, a rota
   de `routes.py` e os testes dela; ajuste `URL` e `CORPO_VALIDO` em `tests/integration/test_comportamento_api.py`
   para a rota JSON.
4. Rode o Passo 5 (ruff, mypy, pytest com 100% de cobertura).

## Proteções (mantenha ao adaptar)

- **Campo desconhecido = 422**, em qualquer nível (`extra="forbid"`). Sem isso, um erro de digitação do cliente
  passa calado e o valor some.
- **Sem conversão de tipos** (`strict=True`): `"2"`, `1.5` e `true` não viram o inteiro `2`/`1`.
- **Limites em todo campo**: tamanho de texto, faixa de número, tamanho mínimo e máximo das listas.
- **Dinheiro em centavos (inteiro)**, nunca float.
- **Texto limpo**: sem caracteres de controle, espaços das pontas removidos, vazio rejeitado.
- **A resposta não devolve dados pessoais** recebidos (nome, e-mail); o erro nunca ecoa a entrada.

## Contrato de erro da entrada JSON (igual ao .NET)

| Situação | Resposta |
|---|---|
| JSON malformado (inclusive `NaN`/`Infinity`) ou aninhado em mais de 64 níveis | 400 `requisicao_invalida` |
| JSON válido com forma errada: campo desconhecido, tipo errado, obrigatório ausente, `null` em obrigatório, fora do limite, lista no lugar de objeto | 422 `entrada_invalida` |

A profundidade e o `NaN`/`Infinity` são verificados pelo template base (`app/core/profundidade_json.py`), antes do
FastAPI ler o corpo: o parser do Python aceitaria milhares de níveis e `NaN` como número.

## Coleção Postman

Na coleção da API (`postman/`), troque a rota de negócio pelo corpo válido do pedido e inclua pedidos de 422 para
campo desconhecido e tipo errado.
