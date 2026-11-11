# Padrão FastAPI + psycopg 3 (async) + psycopg_pool

Leia ao escrever código de aplicação. O padrão segue o desenho dos projetos FastAPI do usuário: Settings com pydantic-settings, recursos criados no `lifespan`, dependências injetáveis e testes sem banco real.

## Dependências

```toml
dependencies = ["psycopg[binary]>=3.2", "psycopg_pool>=3.2", "fastapi", "pydantic-settings"]
```

## Configuração (`app/core/config.py`)

```python
from pydantic import SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    pg_host: str = "127.0.0.1"
    pg_port: int = 5432
    pg_database: str
    pg_user: str
    pg_password: SecretStr            # nunca aparece em log/repr
    pg_sslmode: str = "prefer"        # remoto/nuvem: require ou verify-full
    pg_pool_min: int = 1
    pg_pool_max: int = 5
    pg_statement_timeout_ms: int = 10_000
```

## Pool no lifespan (`app/main.py`)

```python
from contextlib import asynccontextmanager

from fastapi import FastAPI
from psycopg_pool import AsyncConnectionPool

from app.core.config import get_settings


@asynccontextmanager
async def lifespan(app: FastAPI):
    s = get_settings()
    pool = AsyncConnectionPool(
        # conninfo sem senha; a senha vai em kwargs para não aparecer em logs da string
        conninfo=f"host={s.pg_host} port={s.pg_port} dbname={s.pg_database} user={s.pg_user} "
                 f"sslmode={s.pg_sslmode} connect_timeout=5 application_name=motor-credito",
        kwargs={
            "password": s.pg_password.get_secret_value(),
            "options": f"-c statement_timeout={s.pg_statement_timeout_ms}",
        },
        min_size=s.pg_pool_min,
        max_size=s.pg_pool_max,
        open=False,                                   # abrir explicitamente (recomendado)
        check=AsyncConnectionPool.check_connection,   # descarta conexões mortas ao emprestar
        timeout=5,                                    # espera máxima por conexão livre (PoolTimeout)
    )
    await pool.open(wait=False)  # sobe mesmo com o banco fora; /health/ready responde 503 até ele voltar
    # Alternativa: await pool.open(wait=True, timeout=10) para a subida falhar se o banco estiver fora.
    app.state.pg_pool = pool
    try:
        yield
    finally:
        await pool.close()


app = FastAPI(lifespan=lifespan)
```

**Windows:** rode com `uvicorn app.main:app --loop asyncio:SelectorEventLoop`. Sem isso, e sem `--reload`, o psycopg async falha com `ProactorEventLoop`. No Dockerfile Linux não é necessário, mas também não atrapalha.

## Dependência por requisição (`app/api/deps.py`)

```python
from collections.abc import AsyncIterator

from fastapi import Request
from psycopg import AsyncConnection


async def get_conexao(request: Request) -> AsyncIterator[AsyncConnection]:
    # Ao sair sem erro, o pool faz commit; com exceção, faz rollback. Sempre devolve a conexão.
    async with request.app.state.pg_pool.connection() as conn:
        yield conn
```

## Repositório com parâmetros

```python
from psycopg import AsyncConnection, sql
from psycopg.rows import dict_row


async def buscar_cliente(conn: AsyncConnection, cliente_id: int) -> dict | None:
    async with conn.cursor(row_factory=dict_row) as cur:
        await cur.execute("select id, nome, situacao from clientes where id = %s", (cliente_id,))
        return await cur.fetchone()


async def registrar_decisao(conn: AsyncConnection, dados: dict) -> int:
    async with conn.transaction():  # bloco atômico explícito
        cur = await conn.execute(
            "insert into decisoes (cliente_id, resultado) values (%(cliente_id)s, %(resultado)s) "
            "returning id",
            dados,
        )
        (novo_id,) = await cur.fetchone()
        return novo_id


async def contar(conn: AsyncConnection, tabela: str) -> int:
    # Nome dinâmico: Identifier, nunca f-string. Valide 'tabela' contra uma lista permitida.
    consulta = sql.SQL("select count(*) from {}").format(sql.Identifier(tabela))
    cur = await conn.execute(consulta)
    return (await cur.fetchone())[0]
```

Para muitas linhas, use `cur.executemany(...)` ou `COPY` (`async with cur.copy("copy t (a, b) from stdin") as cp: await cp.write_row(...)`), que é muito mais rápido.

## Tradução de erros: falha de banco = 503/504, nunca 500

Padrão validado com o PostgreSQL real na API Python do Motor de Crédito (também no módulo
`python-opcional/bancos` da skill criar-api). Envolva **todo** acesso ao banco com o context manager; os erros de
domínio (`BancoIndisponivel` 503, `BancoExpirou` 504) são respondidos pelo handler de `ErroDeDominio`.

```python
import logging
from collections.abc import Iterator
from contextlib import contextmanager

import psycopg
from psycopg import errors as pgerr
from psycopg_pool import PoolTimeout

from app.core.exceptions import BancoExpirou, BancoIndisponivel

logger = logging.getLogger(__name__)
_PREFIXOS_INDISPONIVEL = ("08", "28", "53", "57P")  # conexão, autenticação, recursos, servidor desligando


@contextmanager
def traduzir_erros_postgres() -> Iterator[None]:
    try:
        yield
    except pgerr.QueryCanceled as exc:            # 57014: statement_timeout
        raise BancoExpirou() from exc
    except PoolTimeout as exc:                    # nenhuma conexão livre no prazo do pool
        logger.error("postgres indisponível", extra={"erro": type(exc).__name__})
        raise BancoIndisponivel() from exc
    except psycopg.OperationalError as exc:
        # Fase de conexão: sqlstate None (no Windows, porta fechada chega como ConnectionTimeout).
        # Conectado: decide a classe do SQLSTATE. Deadlock (40P01) também é OperationalError, mas o banco
        # está no ar: deixe subir (quem chamou repete a transação).
        if exc.sqlstate is None or exc.sqlstate.startswith(_PREFIXOS_INDISPONIVEL):
            logger.error("postgres indisponível", extra={"erro": type(exc).__name__})
            raise BancoIndisponivel() from exc
        raise
    except psycopg.InterfaceError as exc:         # conexão fechada ou pool encerrado
        logger.error("postgres indisponível", extra={"erro": type(exc).__name__})
        raise BancoIndisponivel() from exc
```

Uso no repositório:

```python
async def buscar(self, documento: str) -> list[tuple[object, ...]]:
    with traduzir_erros_postgres():
        async with self._pool.connection() as conn:
            cur = await conn.execute("select ... from credito.sancoes where documento = %s", (documento,))
            return await cur.fetchall()
```

- Erro de programação (`42P01` tabela inexistente, `42601` sintaxe) **continua sendo erro**: vira 500 e aparece no
  log como bug. Unicidade (`23505`) e FK (`23503`): trate no repositório como erro de negócio (409/422).
- Nunca logue `exc.pgconn` (contém a senha) nem `str(exc)`: as mensagens do servidor em português chegam com
  acentuação quebrada e podem trazer trechos do SQL. Registre o tipo e o SQLSTATE.
- **Tempos em escada**: `statement_timeout` (5 s) e espera do pool (`timeout=5`) **menores** que o tempo da
  requisição (ex.: 10 s). Com o banco fora, o pool espera os 5 s antes do `PoolTimeout`; se a requisição tiver
  limite menor, o cliente recebe 504 em vez de 503.

## Health check

```python
@router.get("/health/db")
async def health_db(conn=Depends(get_conexao)) -> dict:
    await conn.execute("select 1")
    return {"postgres": "ok"}
```

Restrinja esse endpoint em produção.

## Testes sem banco real

Sobrescreva a dependência com um objeto falso:

```python
app.dependency_overrides[get_conexao] = lambda: ConexaoFalsa(linhas=[...])
```

Teste de integração com banco real só quando o usuário pedir. Marque com `@pytest.mark.postgres`, use um **banco de teste separado** e rode em transação com rollback ao fim de cada teste.

### Testes de falha sem banco (rodam no CI)

Aponte o driver **real** para uma porta sem servidor: prova que banco fora vira 503, sem precisar de banco no ar.

```python
PORTA_SEM_SERVIDOR = 1

async def test_repositorio_fora_do_ar_vira_banco_indisponivel() -> None:
    repo = await RepositorioPostgres.criar(Settings(pg_port=PORTA_SEM_SERVIDOR, pg_password="x"))
    try:
        with pytest.raises(BancoIndisponivel):
            await repo.buscar("00000000000191")   # PoolTimeout depois de 5 s
    finally:
        await repo.fechar()

@pytest.mark.parametrize("erro", [pgerr.UndefinedTable("x"), pgerr.DeadlockDetected("x")])
def test_erro_de_programacao_continua_sendo_erro(erro: Exception) -> None:
    with pytest.raises(type(erro)), traduzir_erros_postgres():
        raise erro
```

Complete com a API de ponta a ponta: `async with lifespan(app)` com as Settings da porta fechada e
`/health/ready` = 503, `POST` = 503 `banco_indisponivel`.

### Windows: event loop nos testes async

Testes async com psycopg (banco real ou porta fechada) precisam do SelectorEventLoop. No `conftest.py` da pasta
desses testes (pytest-asyncio 1.x):

```python
import asyncio
from collections.abc import Callable, Mapping

import pytest


def pytest_asyncio_loop_factories(
    config: pytest.Config, item: pytest.Item
) -> Mapping[str, Callable[[], asyncio.AbstractEventLoop]]:
    return {"selector": asyncio.SelectorEventLoop}   # funciona também em Linux/macOS
```

## Script de banco e usuário de aplicação (executar como superusuário)

```sql
-- Senha: definir fora do chat (ex.: \password motor_app no psql, que não deixa a senha no histórico)
create role motor_app login;
create database motor_credito owner postgres;
revoke all on database motor_credito from public;
grant connect on database motor_credito to motor_app;

\c motor_credito
create schema credito authorization postgres;
grant usage on schema credito to motor_app;
grant select, insert, update, delete on all tables in schema credito to motor_app;
grant usage, select on all sequences in schema credito to motor_app;
alter default privileges in schema credito
  grant select, insert, update, delete on tables to motor_app;
alter default privileges in schema credito
  grant usage, select on sequences to motor_app;
alter role motor_app in database motor_credito set search_path = credito;
```

Nomes de schema, tabelas e `search_path`: siga a skill **postgresql-naming-schemas**.
