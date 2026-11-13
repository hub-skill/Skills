# Lições do Python (criar-api)

Leia ao gerar ou revisar uma API em Python. As lições de **banco** (psycopg, python-oracledb, pool, sessão somente leitura, CLOB) estão nas skills `conexao-postgresql` e `conexao-oracle` (`references/fastapi.md`).

- `anyio.to_thread.run_sync` **ignora o cancelamento** por padrão: com `fail_after`, o timeout só dispara depois que a thread termina (o teste de 504 recebia 200). Use `abandon_on_cancel=True` (anyio >= 4.1).
- Defina as variáveis de ambiente no `tests/conftest.py` **antes** de importar `app.main`; a configuração é lida na importação.
- Para testar o 500 genérico com `httpx.ASGITransport`, use `raise_app_exceptions=False`; para 504/500, injete a regra de negócio por `Depends` e troque com `app.dependency_overrides`.
- O `json` do Python aceita `NaN`, `Infinity` e `-Infinity` (não existem em JSON) e milhares de níveis de aninhamento (1900 em 4 KB, medido); como o FastAPI ignora campo desconhecido, um JSON fundo num campo extra passava com 200, e o .NET recusa com 400. O template varre o corpo antes (`corpo_json_ilegivel`): mais de 64 níveis ou `N`/`I` maiúsculos fora de textos = 400.
- O FastAPI responde `400 http_400` "There was an error parsing the body" (em inglês) quando o parser de JSON estoura a recursão (JSON aninhado fundo demais). O template trata o 400 do framework como `requisicao_invalida`.
- Pydantic aceita campo desconhecido em silêncio e converte `"2"` em `2` por padrão. Com entrada JSON, use `extra="forbid"` e `strict=True` (base `ModeloDeEntrada` do módulo).
- `ruff format .` formata também scripts fora da API (ex.: um `gerar_pdf.py` na raiz). Formate só `app tests`.
- Exceção não tratada **não passa** pelo middleware do `@app.middleware("http")`: quem a trata é o `ServerErrorMiddleware`, por fora. Sem um `try/except` em volta do `call_next`, o 500 sai sem `X-Request-ID` e sem os headers de segurança. O template captura a exceção no middleware e chama o handler do 500.
- `ruff check -q` não imprime os diagnósticos: num script de validação, confira o código de saída (ou tire o `-q`); uma coluna de lint vazia não é "limpo".
- Escreva `pyproject.toml` com a ferramenta de edição de arquivos, não com `Set-Content` do PowerShell 5.1 (grava BOM/ANSI e quebra o parser TOML).
- O `ruff` do template limita linhas a **110 caracteres, inclusive comentários e docstrings**. Arquivo novo de módulo ou de template com texto longo falha no `ruff check` (E501): quebre a linha; um script de validação só enxerga isso se checar o código de saída.
