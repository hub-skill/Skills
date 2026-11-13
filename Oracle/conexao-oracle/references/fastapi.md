# Padrão FastAPI + python-oracledb (async, modo thin)

Leia ao escrever código de aplicação. O padrão segue o mesmo desenho dos projetos FastAPI do usuário: Settings com pydantic-settings, recursos criados no `lifespan`, dependências injetáveis e testes sem banco real.

## Dependência

```toml
dependencies = ["oracledb>=2.0", "fastapi", "pydantic-settings"]
```

`oracledb>=2.0` traz o suporte async (`create_pool_async`).

## Configuração (`app/core/config.py`)

```python
from pydantic import SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    oracle_dsn: str                     # ex.: localhost:1521/XEPDB1
    oracle_user: str
    oracle_password: SecretStr          # nunca aparece em log/repr
    oracle_pool_min: int = 1
    oracle_pool_max: int = 5
    oracle_call_timeout_ms: int = 10_000
```

Os campos sem valor padrão fazem a aplicação falhar ao subir se o `.env` estiver incompleto. Isso é melhor do que falhar na primeira requisição.

## Pool no lifespan (`app/main.py`)

```python
from contextlib import asynccontextmanager

import oracledb
from fastapi import FastAPI

from app.core.config import get_settings


@asynccontextmanager
async def lifespan(app: FastAPI):
    s = get_settings()
    pool = oracledb.create_pool_async(
        user=s.oracle_user,
        password=s.oracle_password.get_secret_value(),
        dsn=s.oracle_dsn,
        min=s.oracle_pool_min,
        max=s.oracle_pool_max,
        increment=1,
        ping_interval=60,   # testa conexões ociosas antes de reutilizar
        wait_timeout=5000,  # ms esperando conexão livre (com o getmode abaixo)
        getmode=oracledb.POOL_GETMODE_TIMEDWAIT,
    )   # não conecta na criação: a API sobe com o banco fora e /health/ready responde 503
    app.state.oracle_pool = pool
    try:
        yield
    finally:
        await pool.close(force=True)


app = FastAPI(lifespan=lifespan)
```

## Dependência por requisição (`app/api/deps.py`)

```python
from collections.abc import AsyncIterator

import oracledb
from fastapi import Depends, Request

from app.core.config import Settings, get_settings


async def get_conexao(
    request: Request, settings: Settings = Depends(get_settings)
) -> AsyncIterator[oracledb.AsyncConnection]:
    async with request.app.state.oracle_pool.acquire() as conn:
        conn.call_timeout = settings.oracle_call_timeout_ms
        yield conn  # o context manager devolve a conexão ao pool, mesmo com erro
```

## Repositório com bind variables

```python
import oracledb


async def buscar_cliente(conn: oracledb.AsyncConnection, cliente_id: int) -> dict | None:
    with conn.cursor() as cur:
        await cur.execute(
            "select id, nome, situacao from clientes where id = :id",
            id=cliente_id,
        )
        cur.rowfactory = lambda *cols: dict(zip([d[0].lower() for d in cur.description], cols))
        return await cur.fetchone()


async def registrar_decisao(conn: oracledb.AsyncConnection, dados: dict) -> None:
    with conn.cursor() as cur:
        await cur.execute(
            "insert into decisoes (cliente_id, resultado, criado_em) "
            "values (:cliente_id, :resultado, systimestamp)",
            dados,
        )
    await conn.commit()  # commit só depois de tudo dar certo
```

Para muitas linhas, use `executemany` em vez de laço com `execute`. Para leituras grandes, ajuste `cur.arraysize` (ex.: 500).

## Tradução de erros: falha de banco = 503/504, nunca 500

Padrão validado com o Oracle XE real na API Python do Motor de Crédito (também no módulo `python-opcional/bancos`
da skill criar-api). Envolva **todo** acesso ao banco; `BancoIndisponivel` (503) e `BancoExpirou` (504) são
respondidos pelo handler de `ErroDeDominio`.

```python
import logging
from collections.abc import Iterator
from contextlib import contextmanager

import oracledb

from app.core.exceptions import BancoExpirou, BancoIndisponivel

logger = logging.getLogger(__name__)

_EXPIROU = {"DPY-4024", "ORA-01013"}  # call_timeout estourado / comando cancelado
_INDISPONIVEL = {
    "DPY-4005", "DPY-4011",                                   # espera do pool / conexão caiu
    "ORA-01017", "ORA-28000",                                 # senha errada, conta bloqueada
    "ORA-01033", "ORA-01034", "ORA-01089",                    # instância subindo, fora ou descendo
    "ORA-03113", "ORA-03114", "ORA-03135",                    # conexão perdida
    "ORA-12154", "ORA-12170", "ORA-12514", "ORA-12537", "ORA-12541", "ORA-12545",  # listener e rede
}


def codigo_do_erro(exc: oracledb.Error) -> str:
    erro = exc.args[0] if exc.args else None
    return str(getattr(erro, "full_code", type(exc).__name__))   # ex.: "ORA-00942", "DPY-6005"


@contextmanager
def traduzir_erros_oracle() -> Iterator[None]:
    try:
        yield
    except oracledb.Error as exc:
        codigo = codigo_do_erro(exc)
        logger.error("erro oracle", extra={"codigo_oracle": codigo})   # só o código: sem SQL, sem DSN
        if codigo in _EXPIROU:
            raise BancoExpirou() from exc
        if codigo in _INDISPONIVEL or codigo.startswith(("DPY-6", "ORA-125", "ORA-126")):
            raise BancoIndisponivel() from exc
        raise   # ORA-00942, ORA-00904, sintaxe: erro de programação -> 500 e log
```

Uso no repositório:

```python
async def buscar(self, documento: str) -> list[tuple[object, ...]]:
    with traduzir_erros_oracle():
        async with self._pool.acquire() as conn:
            conn.call_timeout = self._timeout_ms
            with conn.cursor() as cur:
                await cur.execute("select ... from sancoes where documento = :documento", documento=documento)
                return await cur.fetchall()
```

Nunca devolva `str(exc)` ao cliente: a mensagem ORA pode expor nomes de tabelas, schema e trechos do SQL.

**Tempos em escada**: `call_timeout` (5 s) e `wait_timeout` do pool (5 s) **menores** que o tempo da requisição
(ex.: 10 s). Com o banco fora, o `acquire()` espera o `wait_timeout`; se a requisição tiver limite menor, o
cliente recebe 504 em vez de 503.

## Health check

```python
@router.get("/health/db")
async def health_db(conn=Depends(get_conexao)) -> dict:
    with conn.cursor() as cur:
        await cur.execute("select 1 from dual")
        await cur.fetchone()
    return {"oracle": "ok"}
```

Proteja ou restrinja esse endpoint em produção, porque ele revela que existe um Oracle por trás.

## Testes sem banco real

Sobrescreva a dependência com um objeto falso, do mesmo jeito que o cliente HTTP é simulado nos testes:

```python
app.dependency_overrides[get_conexao] = lambda: ConexaoFalsa(linhas=[...])
```

Teste de integração com banco real só quando o usuário pedir. Marque com `@pytest.mark.oracle` e rode separado, lendo as credenciais do `.env`.

### Testes de falha sem banco (rodam no CI)

O modo thin não precisa de Instant Client: aponte o driver real para uma porta sem servidor.

```python
from types import SimpleNamespace

PORTA_SEM_SERVIDOR = 1

async def test_repositorio_fora_do_ar_vira_banco_indisponivel() -> None:
    repo = await RepositorioOracle.criar(
        Settings(oracle_dsn=f"127.0.0.1:{PORTA_SEM_SERVIDOR}/XEPDB1", oracle_password="x")
    )
    try:
        with pytest.raises(BancoIndisponivel):
            await repo.verificar()
    finally:
        await repo.fechar()


def _erro_oracle(codigo: str) -> oracledb.DatabaseError:
    return oracledb.DatabaseError(SimpleNamespace(full_code=codigo))   # mesmo formato do driver


@pytest.mark.parametrize("codigo", ["DPY-4024", "ORA-01013"])
def test_timeout_vira_banco_expirou(codigo: str) -> None:
    with pytest.raises(BancoExpirou), traduzir_erros_oracle():
        raise _erro_oracle(codigo)


def test_erro_de_programacao_continua_sendo_erro() -> None:
    with pytest.raises(oracledb.DatabaseError), traduzir_erros_oracle():
        raise _erro_oracle("ORA-00942")
```

## Script de usuário de aplicação (executar como DBA, no PDB)

```sql
-- conectar no PDB (ex.: XEPDB1), nunca no CDB raiz
create user motor_credito identified by "<definir fora do chat>"
  default tablespace users quota 500m on users;
grant create session, create table, create sequence, create view to motor_credito;
-- leitura de tabelas de outro schema: grant objeto a objeto + sinônimo (skill oracle-naming-sinonimos)
```

## Lições vindas da skill criar-api (CLOB)

- python-oracledb: um `str` não vira CLOB como parâmetro de `callproc` (`DPY-3013`; acima de 32767 bytes nem como VARCHAR2). Use `await conn.createlob(oracledb.DB_TYPE_CLOB, texto)`.
