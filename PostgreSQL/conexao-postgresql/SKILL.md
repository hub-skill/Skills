---
name: conexao-postgresql
description: ConexaoPostgreSQL – conecta aplicações Python (FastAPI, scripts, ETL) ao PostgreSQL com o driver psycopg 3 (sync e async) e psycopg_pool. Use sempre que o usuário quiser conectar, testar ou configurar acesso a um banco PostgreSQL (local, servidor, Docker, Supabase, Neon, RDS), montar string de conexão/URI/conninfo, ler postgresql.conf ou pg_hba.conf, criar pool de conexões, colocar credenciais Postgres no .env, configurar SSL (sslmode), ou diagnosticar erros de conexão (autenticação falhou, no pg_hba.conf entry, connection refused, timeout, ProactorEventLoop no Windows), mesmo que ele não cite "psycopg". Para criar ou nomear tabelas, schemas e search_path, use junto a skill postgresql-naming-schemas. Não use para Oracle, MySQL ou SQL Server.
---

# ConexaoPostgreSQL: Python + PostgreSQL

O objetivo é sair de "tenho um PostgreSQL em algum lugar" para uma conexão testada, segura e reutilizável, sem expor senha.

Driver padrão: **psycopg 3** (`pip install "psycopg[binary]" psycopg_pool`). Ele é o sucessor do psycopg2, tem API síncrona e assíncrona e um pool oficial. O extra `[binary]` traz a libpq embutida, sem precisar de PostgreSQL instalado no cliente. Use `asyncpg` só se o projeto já usar; não misture os dois.

## 1. Descobrir os dados de conexão

Antes de perguntar ao usuário, procure na máquina:

1. **Serviço**: no Windows, `Get-Service *postgres*` (ex.: `postgresql-x64-18`). O caminho do serviço (`Get-CimInstance Win32_Service`) mostra a pasta de dados após `-D`. No Linux/macOS: `systemctl status postgresql` ou `pg_lsclusters`. Em Docker: `docker ps` e a porta publicada.
2. **`postgresql.conf`** na pasta de dados: `port` (padrão 5432) e `listen_addresses` (`localhost` só aceita conexões locais; `*` aceita de fora).
3. **`pg_hba.conf`** na mesma pasta: quem pode conectar, de onde e com qual método (`scram-sha-256` é o atual; `md5` é legado; `trust` não pede senha e não deve existir fora de máquina de teste).
4. **Variáveis de ambiente** `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER` e o arquivo de senhas (`%APPDATA%\postgresql\pgpass.conf` no Windows, `~/.pgpass` no Linux). A libpq lê tudo isso automaticamente, e isso pode mascarar o que o código está realmente usando.

Em serviços gerenciados (Supabase, Neon, RDS, Azure, Cloud SQL), a string vem do painel do provedor e quase sempre exige SSL.

## 2. Bancos, usuários e schemas

- O servidor (cluster) tem vários **bancos**. A conexão é sempre a um banco específico; não dá para consultar outro banco na mesma conexão.
- `postgres` é o banco e o superusuário criados na instalação. Servem para administração. A aplicação deve ter **banco e usuário próprios** (ex.: banco `motor_credito`, usuário `motor_app`).
- Dentro do banco ficam os **schemas** (ex.: `public`, `credito`). Nomenclatura, schemas e `search_path` estão na skill **postgresql-naming-schemas**.

## 3. Montar a string de conexão

Dois formatos equivalentes na libpq:

```
# URI
postgresql://<usuario>@<host>:<porta>/<banco>?sslmode=prefer&connect_timeout=5&application_name=<app>

# Palavras-chave (conninfo)
host=<host> port=<porta> dbname=<banco> user=<usuario> sslmode=prefer connect_timeout=5 application_name=<app>

# SQLAlchemy (psycopg 3)
postgresql+psycopg://<usuario>:<senha>@<host>:<porta>/<banco>
```

- Prefira montar a conexão com **parâmetros separados** (`psycopg.connect(host=..., dbname=..., user=..., password=...)`) ou com a URI **sem a senha**, passando `password=` à parte. Senha dentro da URI precisa de *percent-encoding* (`@`, `:`, `/` quebram a URI) e acaba aparecendo em logs.
- `connect_timeout` (segundos): sem ele, um host inacessível pode travar por muito tempo.
- `application_name`: identifica a aplicação em `pg_stat_activity`, o que ajuda muito a diagnosticar conexões presas.
- **SSL (`sslmode`)**: `prefer` (padrão) tenta SSL e aceita sem. Para servidor remoto ou nuvem, use `require` e, de preferência, `verify-full` com `sslrootcert`, que também valida o certificado contra ataques de intermediário. Em `localhost`, `disable` ou `prefer` é aceitável.
- `localhost` pode resolver para IPv6 (`::1`) e IPv4. O `pg_hba.conf` precisa ter regra para o endereço usado. Use `127.0.0.1` para forçar IPv4.

## 4. Validar o servidor sem credenciais reais

Antes de existir usuário e senha, dá para provar que host, porta e `pg_hba.conf` estão certos. Tente conectar com um **usuário inexistente**, que nunca é uma conta real, para não gerar tentativas falhas numa conta de verdade. O script pronto faz isso:

```bash
uv run --no-project --with "psycopg[binary]" python scripts/testar_servidor.py "host=127.0.0.1 port=5432 dbname=postgres"
```

Como interpretar:
- **Servidor pediu senha** (`exc.pgconn.used_password == True`) → servidor no ar, porta certa e o `pg_hba.conf` aceitou o pedido. É o resultado esperado.
- **Servidor recusou antes da senha** (resposta `FATAL` sem pedir senha) → provável falta de regra no `pg_hba.conf` para esse host, usuário ou banco, ou exigência de SSL.
- **`ConnectionTimeout` / recusado** → nada escutando: porta errada, serviço parado, `listen_addresses` restrito ou firewall.

Limitações, para não tirar conclusões erradas:
- O **nome do banco não é validado**: o PostgreSQL autentica antes de checar o banco, então um banco inexistente dá o mesmo resultado de um banco certo. Isso só se confirma com um login real (erro SQLSTATE `3D000`).
- **Na fase de conexão, o psycopg não preenche `sqlstate`** (vem `None`). A mensagem vem no idioma do servidor e, no Windows, frequentemente com acentuação corrompida. Por isso o script usa `used_password` e a palavra `FATAL` (que não é traduzida), não o texto da mensagem.

## 5. Credenciais: nunca no chat, nunca no código, nunca no log

- **Não peça a senha ao usuário na conversa.** O que é escrito no chat fica no histórico. Peça que ele preencha o `.env` direto e use só o nome das variáveis.
- `.env` fora do controle de versão (confira o `.gitignore`), com um `.env.example` sem valores:

```
PG_HOST=127.0.0.1
PG_PORT=5432
PG_DATABASE=nome_do_banco
PG_USER=usuario_da_aplicacao
# preenchida só no .env, nunca no .env.example nem no chat
PG_PASSWORD=
PG_SSLMODE=prefer
PG_POOL_MIN=1
PG_POOL_MAX=5
PG_STATEMENT_TIMEOUT_MS=10000
```

- Na configuração, a senha é `SecretStr` (pydantic-settings), para não aparecer em logs nem em `repr`.
- **Nunca registre `exc.pgconn` nem `repr(exc.pgconn)` em log.** O objeto de erro de conexão do psycopg carrega a senha em texto puro (`password=b'...'`). Registre só o tipo da exceção e um código.
- Usuário da aplicação com **privilégio mínimo**: `LOGIN`, `CONNECT` no banco e `USAGE` + os privilégios necessários no schema. Nunca use o superusuário `postgres` na aplicação. O script de criação está em `references/fastapi.md`.

## 6. Windows + async: a armadilha do event loop

O psycopg assíncrono **não funciona** com o `ProactorEventLoop`, que é o padrão do Python no Windows. O erro é `InterfaceError: Psycopg cannot use the 'ProactorEventLoop' to run in async mode`.

O uvicorn piora isso no Windows: sem `--reload` e sem `--workers`, ele usa Proactor (falha); com `--reload` ou `--workers`, usa Selector (funciona). Ou seja, **a API funciona em desenvolvimento e quebra em produção**. Para evitar:

```bash
# uvicorn (versões com loop personalizado; testado na 0.54)
uvicorn app.main:app --loop asyncio:SelectorEventLoop
```

```python
# scripts async
asyncio.run(main(), loop_factory=asyncio.SelectorEventLoop)   # Python 3.12+
```

Em Linux e macOS o loop padrão já é compatível, mas deixar `--loop asyncio:SelectorEventLoop` também funciona lá. Se o projeto for só síncrono, nada disso se aplica.

## 7. Código

O padrão completo para FastAPI está em `references/fastapi.md`: Settings, `AsyncConnectionPool` no lifespan, dependência por requisição, tradução de erros, health check, testes e o script de usuário. Leia esse arquivo quando for escrever código de aplicação. Em resumo:

- **Um pool por processo**, aberto na inicialização e fechado no desligamento, com `check=AsyncConnectionPool.check_connection` para descartar conexões mortas.
- **Parâmetros sempre** (`%s` ou `%(nome)s`). Nunca monte SQL com f-string ou concatenação: é injeção de SQL. Para nomes dinâmicos (tabela, coluna), use `psycopg.sql.Identifier`, nunca o valor cru.
- **`statement_timeout`** na conexão, para uma consulta travada não segurar a requisição para sempre.
- **Transações**: no psycopg 3, a conexão abre transação implicitamente. Use `async with conn.transaction():` para delimitar, ou deixe o pool fazer commit ao devolver a conexão sem erro.
- **Logs sem dados pessoais e sem credenciais**: registre o SQLSTATE e o tipo do erro, não o SQL com valores.

Para script simples (sem API):

```python
import psycopg
from app.core.config import get_settings

s = get_settings()
with psycopg.connect(host=s.pg_host, port=s.pg_port, dbname=s.pg_database, user=s.pg_user,
                     password=s.pg_password.get_secret_value(), sslmode=s.pg_sslmode,
                     connect_timeout=5, application_name="script-carga") as conn:
    row = conn.execute("select nome from clientes where id = %s", (42,)).fetchone()
    print(row)
```

## 8. Erros comuns

Depois de conectado, use o **SQLSTATE** (`exc.sqlstate` ou as classes em `psycopg.errors`), nunca o texto da mensagem.

| Erro | Significado | O que fazer |
|---|---|---|
| Autenticação de senha falhou (`28P01`) | Usuário ou senha inválidos | Conferir `.env`. No teste com usuário fictício, é o esperado |
| `no pg_hba.conf entry` (`28000`) | Nenhuma regra libera esse host/usuário/banco/SSL | Ajustar `pg_hba.conf` e recarregar (`select pg_reload_conf()`) |
| Banco não existe (`3D000`) | Nome do banco errado | Conferir `PG_DATABASE` (`\l` no psql) |
| Connection refused / `ConnectionTimeout` | Nada escutando no host/porta. No Windows, porta fechada chega como `ConnectionTimeout`, com `sqlstate` None | Serviço parado, porta, `listen_addresses` ou firewall |
| `ProactorEventLoop` (InterfaceError) | Async no Windows com loop errado | Seção 6 |
| Tabela não existe (`42P01`) | Nome errado ou fora do `search_path` | Qualificar com schema ou ajustar `search_path` |
| Permissão negada (`42501`) | Falta `GRANT` | Conceder o privilégio mínimo necessário |
| Consulta cancelada (`57014`) | `statement_timeout` estourou | Otimizar a consulta ou rever o limite |
| Conexões esgotadas (`53300`) | `max_connections` atingido | Reduzir pools, usar PgBouncer |
| Violação de unicidade (`23505`) / FK (`23503`) | Dado duplicado ou referência inexistente | Tratar como erro de negócio (409/422) |
| Serialização (`40001`) / deadlock (`40P01`) | Concorrência | Repetir a transação |
| Conexão caiu (`08006`, `OperationalError`) | Rede ou servidor reiniciou | O pool descarta e reconecta |

Na API, não devolva a mensagem do banco ao cliente: ela pode expor nomes de tabelas e trechos de SQL. Falha de conexão, autenticação ou pool (`08`, `28`, `53`, `57P`, `sqlstate` None, `PoolTimeout`, `InterfaceError`) vira **503**; `57014` vira **504**; o resto (programação, deadlock) não é "banco fora". O context manager `traduzir_erros_postgres()` e os testes com porta fechada estão em `references/fastapi.md` (e no módulo `python-opcional/bancos` da skill criar-api).

## 9. Entrega ao usuário

Ao terminar, mostre:
1. O que foi encontrado: versão, porta, `listen_addresses`, método de autenticação do `pg_hba.conf`.
2. A string de conexão recomendada (sem senha) e, se pedido, os outros formatos.
3. O resultado do teste de servidor, com a limitação de que o nome do banco só é validado num login real.
4. O bloco do `.env.example` e o pedido para o usuário preencher a senha diretamente no `.env`.
5. Se o projeto for async no Windows, o aviso do `--loop asyncio:SelectorEventLoop`.
6. O próximo passo: criar banco e usuário da aplicação (script em `references/fastapi.md`) e fazer o teste de login real.
