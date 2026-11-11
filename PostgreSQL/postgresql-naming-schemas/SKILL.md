---
name: postgresql-naming-schemas
description: Convenções de nomenclatura, tipos de dados, schemas e search_path para PostgreSQL (12 ou superior). Use quando o usuário criar, nomear ou revisar objetos PostgreSQL (tabelas, colunas, constraints, índices, sequences, views, funções, procedures, triggers, tipos, schemas), escrever DDL ou migrações, escolher tipos de coluna, ou quiser consultar tabelas de outro schema sem digitar o nome do schema (o PostgreSQL não tem sinônimos: usa search_path). Para conectar a aplicação ao banco, use junto a skill conexao-postgresql. Não use para Oracle, MySQL ou SQL Server.
---

# Convenções PostgreSQL: nomes, tipos e schemas

Esta skill é exclusiva para **PostgreSQL**. Não há `VARCHAR2`, `NUMBER`, `SYSTIMESTAMP`, `DUAL` nem sinônimos. No PostgreSQL, **schema e usuário são coisas diferentes**: um schema é uma pasta de objetos dentro de um banco; o usuário (role) só recebe permissões sobre ele.

## 0. Projeto Motor de Crédito: onde ficam as tabelas

Nas tarefas do projeto Motor de Crédito (pasta `c:\Felipe\Programacao\Python\APIs\Motor de credito`), use sempre:

| Item | Valor |
|---|---|
| Servidor | PostgreSQL 18 local, `127.0.0.1:5432` |
| Banco | **`motor_credito`** (não o banco `postgres`) |
| Schema | **`credito`** (não o `public`) |
| Dono do schema e das tabelas | `postgres`: cria e altera tabelas (migrações) |
| Usuário da aplicação | `motor_app`: só lê e grava dados; já tem `search_path = credito` |
| Tabelas existentes | `credito.sancoes` (CEIS + CNEP), `credito.peps` |
| Credenciais | `.env` da pasta do projeto (`PG_*`, `PG_ADMIN_*`) |

- Crie toda tabela nova em **`credito.<tabela>`**, com o nome qualificado.
- **Consultas fora da aplicação** (pgAdmin, psql, scripts como `postgres`) precisam do prefixo: `select * from credito.peps`. Sem ele dá `relation "peps" does not exist`, porque o `search_path` do `postgres` é o padrão (`"$user", public`). Só o `motor_app` dispensa o prefixo.
- No pgAdmin, as tabelas ficam em *Databases → motor_credito → Schemas → credito → Tables*, e o Query Tool deve ser aberto a partir do banco `motor_credito`.
- O equivalente no Oracle está na skill oracle-naming-sinonimos (schema `MOTOR_CREDITO`).

## 1. Regras de nomenclatura

As regras de nome são as mesmas da skill oracle-naming-sinonimos, para que projetos nos dois bancos fiquem consistentes:

- snake_case minúsculo com nomes completos, sem abreviações: `data_nascimento`, não `dt_nasc`.
- Tabelas no plural: `clientes`, `vendas`, `parcelas`.
- Colunas no singular: `nome`, `cliente_id`.
- Chave primária sempre `id`.
- Chave estrangeira: `<tabela_singular>_id`, por exemplo `cliente_id` referenciando `clientes.id`.
- Coluna de estado sempre `situacao`.
- Booleanos com `is_` ou `has_`: `is_ativa`.
- Datas e horários terminam em `_em`: `criado_em`, `atualizado_em`. Datas sem hora podem usar `data_`: `data_vencimento`.
- Quantidades terminam em `_qtd` ou descrevem o limite: `parcelas_qtd`, `limite_maximo`.

### Limite de tamanho
**63 bytes** por identificador. Nomes maiores são **truncados em silêncio**, sem erro. Dois nomes longos podem virar o mesmo nome truncado, e o PostgreSQL acusa "já existe" num objeto que você não reconhece. Atenção a acentos: em UTF-8 eles ocupam 2 bytes. Prefira nomes sem acento.

### Maiúsculas e minúsculas
Identificadores sem aspas são convertidos para **minúsculas** (o contrário do Oracle). Escreva sempre em minúsculas e **nunca use aspas duplas** em nomes. `CREATE TABLE "Clientes"` cria uma tabela que só pode ser acessada como `"Clientes"`, com aspas, para sempre. Ferramentas visuais e ORMs às vezes geram nomes com aspas; corrija na origem.

### Palavras reservadas
Evite nomes que exigem aspas: `user`, `order`, `group`, `table`, `limit`, `offset`, `desc`, `check`. Use `usuario`, `pedido`, `grupo`.

### Prefixos de objetos e restrições

| Objeto | Prefixo | Exemplo |
|---|---|---|
| Chave primária | `pk_<tabela>` | `pk_clientes` |
| Chave estrangeira | `fk_<tabela>_<coluna>` | `fk_vendas_cliente_id` |
| Unicidade | `uk_<tabela>_<colunas>` | `uk_clientes_cpf` |
| Check | `ck_<tabela>_<coluna>` | `ck_vendas_situacao` |
| Exclusão | `ex_<tabela>_<colunas>` | `ex_reservas_periodo` |
| Índice | `idx_<tabela>_<colunas>` | `idx_vendas_cliente_id` |
| Sequence avulsa | `seq_<assunto>` | `seq_numero_contrato` (identity cria a sua automaticamente) |
| Trigger | `trg_<tabela>_<evento>` | `trg_clientes_atualiza` |
| Função de trigger | `fn_trg_<acao>` | `fn_trg_atualiza_timestamp` |
| View | `vw_<assunto>` | `vw_clientes_ativos` |
| Materialized view | `mv_<assunto>` | `mv_vendas_resumo` |
| Função | `fn_<acao>` | `fn_calcular_parcela` |
| Procedure | `prc_<acao>` | `prc_fechar_mes` |
| Tipo / enum | `tp_<assunto>` | `tp_situacao_venda` |

**Índices e constraints dividem o espaço de nomes do schema com as tabelas.** Um índice não pode ter o mesmo nome de uma tabela, nem de outro índice, no mesmo schema, mesmo que pertença a outra tabela. Os prefixos acima evitam essas colisões. Nomeie constraints sempre: os nomes automáticos (`clientes_pkey`, `vendas_cliente_id_fkey`) funcionam, mas ficam fora do padrão.

## 2. Tipos de dados

| Uso | Tipo | Evite |
|---|---|---|
| Chave primária | `bigint generated always as identity` | `serial` (legado, permissões e posse da sequence confusas) |
| Texto | `text`, ou `varchar(n)` quando existe um limite real de negócio | `char(n)` (completa com espaços), exceto códigos de tamanho fixo como UF |
| CPF, CNPJ e documentos | **`varchar(20)`**, sempre texto | `char(11)`/`char(14)` e tipos numéricos (perdem zeros à esquerda e não aceitam máscara nem o CNPJ alfanumérico) |
| Dinheiro | `numeric(15,2)` (ou mais casas, se a regra exigir) | `money` (depende do locale), `real`/`double precision` (erros de arredondamento) |
| Taxas e percentuais | `numeric(9,6)` | `float` |
| Inteiros | `integer`; `bigint` para contadores grandes | `smallint` sem motivo |
| Data | `date` | |
| Data e hora | **`timestamptz`** (`timestamp with time zone`), `default now()` | `timestamp` sem fuso para eventos reais |
| Booleano | `boolean` | `char(1)` com 'S'/'N' |
| Identificador externo | `uuid` (`gen_random_uuid()`, nativo no 13+) | `varchar(36)` |
| Dados semiestruturados | `jsonb` | `json` (sem índice, guarda texto cru) |
| Conjunto fixo pequeno | `text` + `check (... in (...))` | `enum` quando os valores mudam (alterar enum é trabalhoso) |
| Binário | `bytea` | arquivos grandes no banco: prefira armazenamento externo |

Sobre `timestamptz`: apesar do nome, ele **não guarda o fuso**. Guarda o instante em UTC e converte para o fuso da sessão na leitura. É o certo para "quando aconteceu". Use `timestamp` sem fuso só para horários "de parede" sem instante definido (ex.: "a loja abre às 08:00").

## 3. Exemplo de tabela em PostgreSQL

```sql
create table credito.clientes (
    id              bigint generated always as identity,
    nome            varchar(120)   not null,
    cpf             varchar(20)    not null,
    data_nascimento date           not null,
    email           text,
    renda_mensal    numeric(15,2),
    situacao        text           not null default 'ATIVO',
    is_bloqueado    boolean        not null default false,
    criado_em       timestamptz    not null default now(),
    atualizado_em   timestamptz    not null default now(),

    constraint pk_clientes          primary key (id),
    constraint uk_clientes_cpf      unique (cpf),
    constraint ck_clientes_situacao check (situacao in ('ATIVO', 'INATIVO', 'BLOQUEADO')),
    constraint ck_clientes_renda    check (renda_mensal >= 0)
);

create index idx_clientes_situacao on credito.clientes (situacao);

comment on table  credito.clientes          is 'Clientes que compram parcelado.';
comment on column credito.clientes.situacao is 'Situação do cliente: ATIVO, INATIVO ou BLOQUEADO.';
```

**Toda chave estrangeira precisa de um índice próprio.** O PostgreSQL cria índice automaticamente para `primary key` e `unique`, mas **não** para `foreign key`. Sem ele, joins ficam lentos e apagar o registro pai varre a tabela filha inteira:

```sql
create table credito.vendas (
    id          bigint generated always as identity,
    cliente_id  bigint        not null,
    valor_total numeric(15,2) not null,
    criado_em   timestamptz   not null default now(),
    constraint pk_vendas            primary key (id),
    constraint fk_vendas_cliente_id foreign key (cliente_id) references credito.clientes (id)
);
create index idx_vendas_cliente_id on credito.vendas (cliente_id);
```

### Atualizar `atualizado_em` automaticamente
`default now()` só vale no insert. Para o update, use uma função de trigger, criada uma vez por schema:

```sql
create or replace function credito.fn_trg_atualiza_timestamp() returns trigger
language plpgsql as $$
begin
    new.atualizado_em := now();
    return new;
end $$;

create trigger trg_clientes_atualiza
    before update on credito.clientes
    for each row execute function credito.fn_trg_atualiza_timestamp();
```

## 4. Schemas e search_path (o lugar dos sinônimos)

O PostgreSQL **não tem sinônimos**. Para digitar só `clientes` em vez de `credito.clientes`, use o **search_path**: a lista de schemas onde o banco procura nomes não qualificados.

Organização recomendada:
- Crie **um schema por domínio** (ex.: `credito`) e não use o `public` para tabelas da aplicação.
- Desde o PostgreSQL 15, usuários comuns já não podem criar objetos no `public`. Em versões anteriores, rode `revoke create on schema public from public;`.

Ordem de preferência para definir o search_path:
1. **Por usuário, no banco**: `alter role motor_app in database motor_credito set search_path = credito;`. Vale para toda nova sessão desse usuário nesse banco. É o equivalente mais próximo de "sinônimos para todas as tabelas".
2. **Por banco**: `alter database motor_credito set search_path = credito, public;`. Vale para todos os usuários.
3. **Na conexão**: `options=-c search_path=credito` na string de conexão (skill conexao-postgresql).
4. **Na sessão**: `set search_path = credito;`, para uso pontual no psql.

Regras:
- O search_path **não concede acesso**. O usuário precisa de `grant usage on schema` e dos privilégios nas tabelas.
- Se dois schemas do search_path tiverem tabela com o mesmo nome, vale o primeiro da lista. Evite nomes repetidos entre schemas.
- A mudança com `alter role ... set` só vale para **novas** conexões. Conexões abertas em pool continuam com o valor antigo até serem recicladas.
- Confira com `show search_path;` e `select current_schema();`.

**Segurança:** em funções `security definer`, fixe o search_path (`set search_path = credito, pg_temp`) na definição da função. Sem isso, quem chama pode criar um objeto com o mesmo nome num schema próprio e sequestrar a execução.

## 5. Permissões padrão

Conceder em tabelas existentes não cobre as tabelas criadas depois. Use também `alter default privileges`:

```sql
grant usage on schema credito to motor_app;
grant select, insert, update, delete on all tables in schema credito to motor_app;
grant usage, select on all sequences in schema credito to motor_app;
alter default privileges in schema credito
    grant select, insert, update, delete on tables to motor_app;
alter default privileges in schema credito
    grant usage, select on sequences to motor_app;
```

Os `default privileges` valem para objetos criados **pelo usuário que executou o comando**. Rode-os com o mesmo usuário que fará as migrações (o dono do schema), ou adicione `for role <dono>`.

## 6. Documentação

- `comment on table` e `comment on column` para toda tabela e coluna.
- Leitura do dicionário: `obj_description('credito.clientes'::regclass)` e `col_description('credito.clientes'::regclass, <número da coluna>)`, ou `\d+ credito.clientes` no psql.

## 7. Scripts de deploy e migrações

- Use nomes **qualificados** (`credito.clientes`) em scripts de deploy e migrações, para que o resultado não dependa do search_path de quem executa.
- Ferramenta de migração (Alembic, Flyway, Liquibase): cada mudança em arquivo versionado, nunca alteração manual em produção.
- `create index concurrently` em tabelas grandes com tráfego, para não bloquear escritas. Ele não pode rodar dentro de transação.
- Adicionar coluna com `default` constante é instantâneo desde o PostgreSQL 11. Adicionar `not null` em tabela grande exige varredura: faça em etapas (constraint `check ... not valid`, depois `validate constraint`).
- O search_path de cada usuário fica nos scripts de acesso, separado dos scripts de criação de tabelas.

## 8. Checklist antes de publicar um objeto

- [ ] Nome em snake_case minúsculo, completo, sem abreviação e sem acento
- [ ] Tabela no plural, coluna no singular
- [ ] Sem aspas duplas e sem palavra reservada
- [ ] Nome com até 63 bytes
- [ ] Constraints e índices nomeados com prefixo padrão, sem colidir com outros nomes do schema
- [ ] Chave primária `bigint generated always as identity`
- [ ] Dinheiro em `numeric`, data e hora em `timestamptz`
- [ ] CPF, CNPJ e documentos em `varchar(20)`
- [ ] Índice criado para cada chave estrangeira
- [ ] Tabela num schema da aplicação, não no `public`
- [ ] `comment on` para tabela e colunas
- [ ] `grant` + `alter default privileges` + search_path do usuário da aplicação
