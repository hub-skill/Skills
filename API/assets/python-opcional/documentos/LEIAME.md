# Módulo opcional: CPF e CNPJ (Python)

Use quando a API recebe CPF ou CNPJ. Extraído da API Python do Motor de Crédito, testado com dados reais da CGU.

## O que traz

- `normalizar_documento(valor)`: remove pontuação (`.`, `-`, `/`, espaços), valida os dígitos verificadores e
  devolve `(documento, tipo)` com `tipo` = `"CPF"` ou `"CNPJ"`. Lança `ValueError` se inválido.
- **CNPJ alfanumérico** (Receita Federal, a partir de 2026): 12 posições `[0-9A-Z]` + 2 dígitos; cada caractere vale
  o código ASCII menos 48. Letras minúsculas viram maiúsculas.
- Recusa dígitos Unicode (ex.: `٣`, por isso a regex usa `[0-9]` e não `\d`), sequências repetidas e outros caracteres.
- `mascarar(documento, tipo)`: CPF vira `***.456.789-**`; CNPJ, que é público, só é formatado.
- `miolo_cpf(cpf)`: os 6 dígitos que ficam visíveis no CPF mascarado.

## Como incluir

1. Copie `app/services/documentos.py` para `app/services/` e `tests/unit/test_documentos.py` para `tests/unit/`
   (usa `hypothesis`, já nas dependências de desenvolvimento).
2. No schema de entrada:

```python
documento: str = Field(..., min_length=11, max_length=20, description="CPF ou CNPJ, com ou sem pontuação")

@field_validator("documento")
@classmethod
def validar_documento(cls, valor: str) -> str:
    normalizar_documento(valor.strip())  # lança ValueError se inválido -> 422 entrada_invalida
    return valor.strip()
```

3. Na rota, use o valor normalizado nas consultas e **nunca devolva o CPF completo**: responda com `mascarar(...)`
   e não registre o documento em log.
4. Banco de dados: CPF, CNPJ e documentos como texto de tamanho 20 (`varchar(20)` / `VARCHAR2(20)`), nunca número.
