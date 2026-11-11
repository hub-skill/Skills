# Módulo opcional: CPF e CNPJ

Use quando a API recebe CPF ou CNPJ. Extraído do Motor-de-Credito-Net, onde foi testado com dados reais da CGU.

## O que traz

- `ValidadorDeDocumento.TentarNormalizar(entrada, out doc)`: remove pontuação (`.`, `-`, `/`, espaços), valida os
  dígitos verificadores e devolve `DocumentoNormalizado(Valor, Tipo)` com `Tipo` = `Cpf` ou `Cnpj`.
- **CNPJ alfanumérico** (Receita Federal, a partir de 2026): 12 posições `[0-9A-Z]` + 2 dígitos; cada caractere vale
  o código ASCII menos 48. Letras minúsculas viram maiúsculas.
- Recusa dígitos Unicode (ex.: `٣`), sequências repetidas (`11111111111`) e qualquer outro caractere.
- `Mascarar(doc)`: CPF vira `***.456.789-**` (formato da CGU); CNPJ, que é público, só é formatado.
- `MioloCpf(cpf)`: os 6 dígitos que ficam visíveis no CPF mascarado.

## Como incluir

1. Copie `src/Documentos/ValidadorDeDocumento.cs` para `src/ProjetoApi/Documentos/`.
2. Copie `tests/ValidadorDeDocumentoTests.cs` para `tests/ProjetoApi.UnitTests/`
   (usa `FsCheck.Xunit`, já referenciado no projeto de testes unitários).
3. No validador FluentValidation da entrada:

```csharp
RuleFor(x => x.Documento)
    .NotEmpty()
    .Length(11, 20) // cabe a máscara (CNPJ formatado tem 18)
    .Must(d => ValidadorDeDocumento.TentarNormalizar(d, out _))
    .WithMessage("documento não é um CPF ou CNPJ válido");
```

4. No endpoint, use o valor normalizado (`doc.Valor`) nas consultas e **nunca devolva o CPF completo**: responda
   com `Mascarar(doc)` e não registre o documento em log.
5. Banco de dados: grave CPF, CNPJ e documentos como texto de tamanho 20 (`varchar(20)` / `VARCHAR2(20)`), nunca
   como número (perde zeros à esquerda e não aceita o CNPJ alfanumérico).
