# Módulo opcional: PostgreSQL e Oracle (Python)

Use quando a API consulta PostgreSQL (psycopg 3) e/ou Oracle (python-oracledb). Reúne o que foi validado nas APIs
Python e .NET do Motor de Crédito, com os dois bancos reais. Garante que **falha de banco nunca vira erro 500**.

## O que traz

| Arquivo | Conteúdo |
|---|---|
| `app/bancos/erros.py` | `BancoIndisponivel` (503, `banco_indisponivel`) e `BancoExpirou` (504, `banco_timeout`) |
| `app/bancos/postgres.py` | `montar_conninfo` (sem senha), `options_sessao` (read only + `statement_timeout`) e `traduzir_erros_postgres()` |
| `app/bancos/saude.py` | `resposta_pronto(verificar, banco)`: corpo do `/health/ready` com banco (200 `{"status":"ok","banco":"postgres"}`; 503 `{"status":"banco_indisponivel"}`) |
| `app/bancos/oracle.py` | `traduzir_erros_oracle()` (DPY-6xxx de rede, `ORA-01017`, `DPY-4024` de timeout...) |
| `tests/unit/test_falhas_de_banco.py`, `tests/unit/test_saude_banco.py` | Drivers reais contra porta sem servidor (roda no CI, sem banco) e classificação dos códigos |

Copie só o que usar: sem Oracle, apague `oracle.py` e os testes de Oracle; sem PostgreSQL, o inverso.

## Como incluir

1. Copie `app/bancos/` para `app/bancos/` da API (crie `app/bancos/__init__.py`) e
   `tests/unit/test_falhas_de_banco.py` e `tests/unit/test_saude_banco.py` para `tests/unit/`.
2. Dependências no `pyproject.toml`: `"psycopg[binary]>=3.2"`, `"psycopg-pool>=3.2"` e/ou `"oracledb>=2.0"`.
3. Os erros herdam de `ErroDeDominio`: o handler que já existe em `main.py` responde 503/504 no formato padrão.
4. Nos repositórios, envolva cada acesso ao banco e use sempre parâmetros (nunca concatene valores no SQL):

```python
async def buscar(self, documento: str) -> list[Cliente]:
    with traduzir_erros_postgres():
        async with self._pool.connection() as conn:
            cur = await conn.execute("select id, nome from app.clientes where documento = %s", (documento,))
            return [Cliente(*linha) for linha in await cur.fetchall()]
```

   Com pool, trate também `psycopg_pool.PoolTimeout` (fila do pool esgotada) como `BancoIndisponivel`.
   Mantenha a espera do pool (`timeout=5` no psycopg_pool, `wait_timeout=5000` no oracledb) e o tempo de cada
   consulta **menores** que `REQUEST_TIMEOUT_SECONDS`: senão, com o banco fora, o cliente recebe 504 em vez de 503.
   Teste de ponta a ponta recomendado: `async with lifespan(app)` com o banco numa porta fechada, e
   `/health/ready` e a rota de negócio respondendo 503 (exemplo nos testes `tests/falhas` do Motor de Crédito).

5. Pool no `lifespan` e `/health/ready` consultando o banco (devolva 503 quando a consulta falhar):

```python
pool = AsyncConnectionPool(
    conninfo=montar_conninfo(s.pg_host, s.pg_port, s.pg_database, s.pg_user),
    kwargs={"password": s.pg_password.get_secret_value(),
            "options": options_sessao(somente_leitura=True)},
    open=False, check=AsyncConnectionPool.check_connection,
)
await pool.open(wait=False)
```

   No `main.py`, troque o corpo do `/health/ready` do template base (genérico, só `{"status":"ok"}`) pelo do módulo:

```python
@app.get("/health/ready", include_in_schema=False)
async def ready() -> JSONResponse:
    async def consulta() -> object:
        with traduzir_erros_postgres():
            async with pool.connection() as conn:
                return await conn.execute("select 1")

    return await resposta_pronto(consulta, "postgres")  # ou "oracle"
```

   Atualize o teste `test_health_e_cabecalhos_de_seguranca` e o pedido `health/ready` da coleção Postman.

6. **Windows + psycopg async**: rode o uvicorn com `--loop asyncio:SelectorEventLoop` (sem isso a API quebra em
   produção e funciona em desenvolvimento com `--reload`). Nos testes async com banco real, use o hook
   `pytest_asyncio_loop_factories` (ver skill conexao-postgresql).
7. Credenciais no `.env` (`SecretStr`), usuário de aplicação com privilégio mínimo. Nunca registre `exc.pgconn`
   em log: ele contém a senha em texto puro. Para o banco em si, siga as skills de nomenclatura.

## Não coberto pelos testes automáticos

O timeout real (`statement_timeout`, `DPY-4024`, `ORA-01013`) exige uma consulta lenta de verdade; a tradução é
testada com a exceção do driver, mas só é exercitada de ponta a ponta contra um banco real.
