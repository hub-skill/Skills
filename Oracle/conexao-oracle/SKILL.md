---
name: conexao-oracle
description: ConexaoOracle – conecta aplicações Python (FastAPI, scripts, ETL) ao Oracle Database com o driver python-oracledb em modo thin. Use sempre que o usuário quiser conectar, testar ou configurar acesso a um banco Oracle, montar string de conexão/DSN, ler o tnsnames.ora, escolher entre CDB e PDB (XE x XEPDB1), criar pool de conexões, colocar credenciais Oracle no .env, ou diagnosticar erros ORA-/DPY- de conexão (ORA-01017, ORA-12514, ORA-12541), mesmo que ele não cite "python-oracledb". Para criar ou nomear tabelas e sinônimos, use junto a skill oracle-naming-sinonimos. Para APIs C# (.NET) com ODP.NET, use as lições de references/dotnet.md. Não use para PostgreSQL, MySQL ou SQL Server.
---

# ConexaoOracle: Python + Oracle Database

O objetivo é sair de "tenho um Oracle em algum lugar" para uma conexão testada, segura e reutilizável, sem expor senha e sem gravar dados de aplicação no lugar errado.

Driver padrão: **python-oracledb** (sucessor oficial do cx_Oracle) em **modo thin**. O modo thin é Python puro: não exige Oracle Instant Client, funciona com `pip install oracledb` e suporta async. Ele conecta em Oracle Database 12.1 ou superior. Use o modo thick (`oracledb.init_oracle_client()`) só quando o thin não atender: banco anterior ao 12.1 ou recursos que o thin não cobre, como alguns casos de criptografia nativa de rede.

## 1. Descobrir os dados de conexão

Antes de perguntar ao usuário, procure na máquina:

1. Variáveis `TNS_ADMIN` e `ORACLE_HOME`.
2. Arquivo `tnsnames.ora` em `<ORACLE_HOME>\network\admin`, em `C:\app\<usuario>\product\<versao>\...\network\admin` ou em pastas de Instant Client. Ignore a pasta `sample\`, que só tem modelos.
3. Se o banco for local, confirme os serviços que o listener anuncia: `lsnrctl services` (Windows: `C:\app\...\bin\lsnrctl.exe`). O tnsnames pode estar desatualizado; o listener mostra o que existe de fato. No Windows, os serviços `OracleService<SID>` e `Oracle...TNSListener` precisam estar `Running`.

De cada entrada do tnsnames, extraia `HOST`, `PORT` e `SERVICE_NAME` (ou `SID`, em bancos antigos).

## 2. Escolher o serviço certo: CDB x PDB

Do 12c em diante, o banco costuma ser *multitenant*:

| Serviço | O que é | Uso |
|---|---|---|
| CDB raiz (ex.: `XE`, `ORCL`, `ORCLCDB`) | Contêiner raiz | Só administração. Não crie usuários comuns nem tabelas de aplicação aqui |
| PDB (ex.: `XEPDB1`, `ORCLPDB1`) | Banco plugável | Onde ficam os schemas e as tabelas da aplicação |

Por isso, recomende o **PDB** para a aplicação. É comum o tnsnames ter alias só para o CDB (no Oracle XE, a entrada `XE` aponta para a raiz). Nesse caso, monte o DSN do PDB direto, sem depender do arquivo.

## 3. Montar a string de conexão

Prefira o **Easy Connect**, que não depende do tnsnames.ora:

```
<host>:<porta>/<service_name>          ex.: localhost:1521/XEPDB1
```

Outros formatos, quando o usuário pedir ou o ambiente exigir:

```
# Descritor completo (mesmo formato do tnsnames)
(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=<host>)(PORT=<porta>))(CONNECT_DATA=(SERVER=DEDICATED)(SERVICE_NAME=<servico>)))

# Alias do tnsnames (thin mode precisa saber onde está o arquivo)
oracledb.connect(user=..., password=..., dsn="<ALIAS>", config_dir=r"<pasta do tnsnames.ora>")

# SQLAlchemy
oracle+oracledb://<usuario>:<senha>@<host>:<porta>/?service_name=<servico>
```

Para Oracle Cloud (Autonomous Database), a conexão usa *wallet*: passe `config_dir`, `wallet_location` e `wallet_password`, todos vindos do `.env`.

## 4. Validar o serviço sem credenciais reais

Antes de existir usuário e senha, dá para provar que host, porta e serviço estão certos. Tente conectar com um **usuário inexistente**:

- `ORA-01017` (usuário/senha inválidos) → listener respondeu e o serviço existe. É o resultado esperado.
- `ORA-12514` / `DPY-6001` → o serviço não está registrado no listener: nome do serviço errado.
- `ORA-12541` / `DPY-6005` → nada escutando no host/porta, firewall, ou banco/listener parado.

Use um nome claramente fictício. Nunca use um usuário real, para não gastar tentativas de login nem bloquear a conta. O script pronto faz isso para vários DSNs de uma vez:

```bash
uv run --no-project --with oracledb python scripts/testar_servico.py localhost:1521/XEPDB1 localhost:1521/XE
```

## 5. Credenciais: nunca no chat, nunca no código

- **Não peça a senha ao usuário na conversa.** O que é escrito no chat fica no histórico. Peça que ele preencha o `.env` direto e use só o nome das variáveis.
- `.env` fora do controle de versão (confira o `.gitignore`), com um `.env.example` sem valores:

```
ORACLE_DSN=localhost:1521/XEPDB1
ORACLE_USER=
ORACLE_PASSWORD=
ORACLE_POOL_MIN=1
ORACLE_POOL_MAX=5
ORACLE_CALL_TIMEOUT_MS=10000
```

- Na configuração, a senha é `SecretStr` (pydantic-settings), para não aparecer em logs nem em `repr`.
- **Senha de usuário Oracle com no máximo 30 caracteres** (nesta instalação, 21c XE): acima disso `CREATE USER ... IDENTIFIED BY` falha com `ORA-00972: identificador muito longo`, mensagem que não cita senha. Gere senhas alfanuméricas de até 24 caracteres.
- Usuário da aplicação com **privilégio mínimo**: `CREATE SESSION` + o que a aplicação precisa. Para ler tabelas de outro schema, conceda `SELECT` objeto a objeto e crie sinônimos, seguindo a skill **oracle-naming-sinonimos**. Nunca use `SYSTEM`/`SYS` na aplicação.

## 6. Código

Para **C# (.NET, ODP.NET)**, leia `references/dotnet.md` (as lições do driver; o código pronto está no módulo `bancos` da skill criar-api). O padrão completo para FastAPI está em `references/fastapi.md`: Settings, pool no lifespan, dependência por requisição, health check e testes. Leia esse arquivo quando for escrever código de aplicação. Em resumo:

- **Um pool por processo**, criado na inicialização e fechado no desligamento. Abrir uma conexão por requisição é lento e esgota o banco.
- **Bind variables sempre** (`:cpf`, `:id`). Nunca monte SQL concatenando ou com f-string: é injeção de SQL e também impede o Oracle de reaproveitar o plano de execução.
- **Timeout por chamada** (`connection.call_timeout`, em milissegundos), para uma consulta travada não segurar a requisição para sempre.
- **Transações explícitas**: `commit()` só depois de tudo dar certo; em erro, `rollback()`. O context manager do pool devolve a conexão mesmo em exceção.
- **Logs sem dados pessoais e sem senha**: registre o código do erro (`ORA-xxxxx`), não o SQL com valores nem o DSN com credenciais.

Para script simples (sem API), a forma mínima:

```python
import oracledb
from app.core.config import get_settings

s = get_settings()
with oracledb.connect(user=s.oracle_user, password=s.oracle_password.get_secret_value(),
                      dsn=s.oracle_dsn) as conn:
    conn.call_timeout = s.oracle_call_timeout_ms
    with conn.cursor() as cur:
        cur.execute("select nome from clientes where id = :id", id=42)
        print(cur.fetchone())
```

## 7. Erros comuns

| Erro | Significado | O que fazer |
|---|---|---|
| ORA-01017 | Usuário ou senha inválidos | Conferir `.env`. Lembrar que a senha é case-sensitive. No teste de serviço, é o resultado esperado |
| ORA-28000 | Conta bloqueada | DBA: `alter user <u> account unlock` |
| ORA-28001 | Senha expirada | Trocar a senha. Para usuários de aplicação, avaliar um *profile* sem expiração |
| ORA-12514 / DPY-6001 | Serviço desconhecido no listener | Corrigir o service name (ver `lsnrctl services`). Conferir CDB x PDB |
| ORA-12541 / DPY-6005 | Sem listener no host/porta | Iniciar o listener ou o serviço Windows, conferir host, porta e firewall |
| ORA-12170 / DPY-6000 | Timeout de rede | Rede, VPN ou firewall |
| ORA-01045 | Usuário sem `CREATE SESSION` | `grant create session to <u>` |
| ORA-00942 | Tabela ou view não existe (ou sem permissão) | Conferir o schema, o grant de `SELECT` e o sinônimo |
| ORA-00972 | Identificador muito longo | Em `CREATE USER ... IDENTIFIED BY`, a senha tem mais de 30 caracteres: use até 24. Em objetos, nome acima do limite |
| ORA-03113 / DPY-4011 | Conexão caiu | O pool descarta e reconecta; avaliar `ping_interval` |
| DPY-4005 | Espera por conexão do pool esgotada (ou banco fora) | Rever `max` do pool e o `wait_timeout` |
| DPY-4024 / ORA-01013 | `call_timeout` estourou / comando cancelado | Otimizar a consulta ou rever o limite (504) |
| DPY-3010 | Versão do banco não suportada em modo thin | Usar modo thick com Instant Client |

Na API, transforme esses erros em respostas sem detalhes internos: conexão, login (ORA-01017, ORA-28000), listener e rede (DPY-6xxx, ORA-125xx/126xx) → **503**; DPY-4024/ORA-01013 → **504**; ORA-00942 ou erro de SQL → 500 com log do código. Nunca devolva a mensagem ORA ao cliente. O context manager `traduzir_erros_oracle()` e os testes com porta fechada estão em `references/fastapi.md` (e no módulo `python-opcional/bancos` da skill criar-api).

## 8. Entrega ao usuário

Ao terminar, mostre:
1. Os serviços encontrados e o recomendado (PDB), com o motivo.
2. O DSN em Easy Connect e, se pedido, nos outros formatos.
3. O resultado do teste de serviço (códigos ORA/DPY).
4. O bloco do `.env.example` e o pedido para o usuário preencher a senha diretamente no `.env`.
5. O próximo passo: teste de login real quando houver usuário, ou o script de criação do usuário de aplicação (seguindo oracle-naming-sinonimos).
