---
name: oracle-naming-sinonimos
description: Convenções de nomenclatura, tipos de dados e uso de sinônimos para Oracle Database (12c ou superior). Use quando o usuário criar, nomear ou revisar objetos Oracle (tabelas, colunas, constraints, índices, sequences, views, packages, triggers, sinônimos), ou quando quiser consultar tabelas de outro schema sem digitar o nome do schema. Não use para PostgreSQL, MySQL ou SQL Server.
---

# Convenções Oracle: nomes, tipos e sinônimos

Esta skill é exclusiva para **Oracle Database**. Sintaxes como `CREATE SCHEMA IF NOT EXISTS`, `TIMESTAMP` sem precisão, `COMMENT ON` com aspas simples de outros SGBDs não se aplicam aqui. No Oracle, um schema é um usuário do banco.

## 0. Projeto Motor de Crédito: onde ficam as tabelas

Nas tarefas do projeto Motor de Crédito (pasta `c:\Felipe\Programacao\Python\APIs\Motor de credito`), use sempre:

| Item | Valor |
|---|---|
| Banco | Oracle 21c XE local, banco plugável **`XEPDB1`** (`localhost:1521/XEPDB1`), nunca o CDB `XE` |
| Schema | **`MOTOR_CREDITO`**: dono das tabelas, *schema-only* (sem login) |
| Usuário da aplicação | `MOTOR_APP`: só `CREATE SESSION` + grants nas tabelas, acessa por sinônimos privados |
| Administrador | `SYSTEM`: cria tabelas, grants e sinônimos |
| Tabelas existentes | `MOTOR_CREDITO.SANCOES` (CEIS + CNEP), `MOTOR_CREDITO.PEPS` |
| Credenciais | `.env` da pasta do projeto (`ORACLE_*`, `ORACLE_ADMIN_*`) |

- Crie toda tabela nova como **`motor_credito.<tabela>`** (conectado como `SYSTEM`) e, em seguida:
  `GRANT SELECT, INSERT, UPDATE, DELETE ON motor_credito.<tabela> TO motor_app` e
  `CREATE OR REPLACE SYNONYM motor_app.<tabela> FOR motor_credito.<tabela>`.
- **Consultas fora da aplicação** (SQL Developer, SQL*Plus como `SYSTEM`) precisam do schema: `select * from motor_credito.peps`. Só o `MOTOR_APP` dispensa o prefixo, por causa dos sinônimos.
- No SQL Developer, use a conexão **`Motor_Credito`** (SYSTEM, *Nome do Serviço* `XEPDB1`). A conexão `teste` usa SID `xe` (CDB raiz) e dá `ORA-00942` nas tabelas do projeto. Conectado como `SYSTEM`, as tabelas ficam em *Outros Usuários → MOTOR_CREDITO → Tabelas*; conectado como `MOTOR_APP`, aparecem em *Sinônimos*.
- O equivalente no PostgreSQL está na skill postgresql-naming-schemas (schema `credito`).

## 1. Regras de nomenclatura

- snake_case minúsculo com nomes completos, sem abreviações: `data_nascimento`, não `dt_nasc`.
- Tabelas no plural: `alunos`, `turmas`, `matriculas`.
- Colunas no singular: `nome`, `turma_id`.
- Chave primária sempre `id`.
- Chave estrangeira: `<tabela_singular>_id`, por exemplo `aluno_id` referenciando `alunos.id`.
- Coluna de estado sempre `situacao`.
- Booleanos com `is_` ou `has_`: `is_ativa`.
- Datas e horários terminam em `_em`: `criado_em`, `atualizado_em`.
- Quantidades terminam em `_qtd` ou descrevem o limite: `capacidade_maxima`.

### Limite de tamanho
- Oracle 12.2 ou superior: 128 bytes por identificador.
- Versões anteriores: 30 bytes.

### Maiúsculas e minúsculas
Identificadores sem aspas são armazenados em MAIÚSCULAS. Escreva em snake_case minúsculo no código, mas nunca use nomes entre aspas duplas. Nomes entre aspas ficam case-sensitive e geram divergência difícil de depurar.

### Prefixos de objetos e restrições

| Objeto | Prefixo | Exemplo |
|---|---|---|
| Chave primária | `pk_` | `pk_alunos` |
| Chave estrangeira | `fk_<tabela>_<coluna>` | `fk_matriculas_aluno_id` |
| Unicidade | `uk_<tabela>_<colunas>` | `uk_alunos_cpf` |
| Check | `ck_<tabela>_<coluna>` | `ck_alunos_situacao` |
| Índice | `idx_<tabela>_<colunas>` | `idx_matriculas_turma_id` |
| Sequence | `seq_<tabela>` | `seq_alunos` (use apenas se não usar identity) |
| Trigger | `trg_<tabela>_<evento>` | `trg_alunos_atualiza` |
| View | `vw_<assunto>` | `vw_alunos_ativos` |
| Materialized view | `mv_<assunto>` | `mv_matriculas_resumo` |
| Package | `pkg_<assunto>` | `pkg_matricula` |
| Procedure | `prc_<acao>` | `prc_matricular_aluno` |
| Função | `fn_<acao>` | `fn_calcular_media` |

Sinônimos não recebem prefixo. O objetivo é que a consulta fique natural: `SELECT * FROM alunos`.

## 2. Tipos de dados

- `VARCHAR2(n)` em vez de `VARCHAR`. Defina o tamanho sempre.
- CPF, CNPJ e documentos: sempre **`VARCHAR2(20)`**. Nunca `CHAR(11)`/`CHAR(14)` nem `NUMBER`: tipos numéricos perdem zeros à esquerda, e tamanho fixo não aceita máscara nem o CNPJ alfanumérico.
- `NUMBER(p,s)` para decimais e inteiros. Para inteiros de identidade, use `NUMBER`.
- `DATE` já carrega data e hora. Use `TIMESTAMP` quando precisar de frações de segundo.
- `TIMESTAMP WITH TIME ZONE` quando o fuso horário importa.
- `CLOB` para textos longos e `BLOB` para binários. Evite-os em colunas consultadas com frequência.
- Chaves primárias: `NUMBER GENERATED ALWAYS AS IDENTITY` (Oracle 12c ou superior).

## 3. Exemplo de tabela em Oracle

```sql
CREATE TABLE academico.alunos (
    id                NUMBER GENERATED ALWAYS AS IDENTITY,
    nome              VARCHAR2(120)  NOT NULL,
    cpf               VARCHAR2(20)   NOT NULL,
    data_nascimento   DATE           NOT NULL,
    email             VARCHAR2(255),
    situacao          VARCHAR2(20)   DEFAULT 'ATIVO' NOT NULL,
    criado_em         TIMESTAMP      DEFAULT SYSTIMESTAMP NOT NULL,
    atualizado_em     TIMESTAMP      DEFAULT SYSTIMESTAMP NOT NULL,

    CONSTRAINT pk_alunos            PRIMARY KEY (id),
    CONSTRAINT uk_alunos_cpf        UNIQUE (cpf),
    CONSTRAINT ck_alunos_situacao   CHECK (situacao IN ('ATIVO', 'INATIVO', 'TRANCADO'))
);

COMMENT ON TABLE  academico.alunos                 IS 'Cadastro de alunos da escola.';
COMMENT ON COLUMN academico.alunos.situacao        IS 'Situação atual do aluno: ATIVO, INATIVO ou TRANCADO.';
```

## 4. Schema implícito com sinônimos

O objetivo é digitar só `alunos` no dia a dia, sem `academico.`.

Ordem de preferência:
1. **Conectar com o usuário do schema**: `SELECT * FROM alunos` funciona direto.
2. **Sinônimo privado** para cada tabela, quando o usuário precisa acessar outro schema. Gere um script com um `CREATE OR REPLACE SYNONYM` por tabela, seguindo o padrão abaixo.
3. **`ALTER SESSION SET CURRENT_SCHEMA = ACADEMICO`** para uma sessão pontual.
4. **Trigger de logon**: funciona, mas é difícil de depurar e pode impedir o login se houver erro.

Regras para sinônimos:
- Use sinônimos **privados**. Evite `CREATE PUBLIC SYNONYM`, porque ele fica visível para todos os usuários e exige privilégio de DBA.
- Um sinônimo **não concede acesso**. O dono do schema precisa emitir `GRANT` para o usuário.
- O sinônimo pode ser criado antes da tabela existir, mas a consulta só funciona depois que a tabela for criada.
- Se o usuário tiver uma tabela com o mesmo nome do sinônimo, a tabela local tem prioridade. Evite esse conflito.
- Nomeie o sinônimo igual à tabela para não exigir memorização extra.

Padrão do script:

```sql
CREATE OR REPLACE SYNONYM alunos FOR academico.alunos;

-- Verificação
SELECT synonym_name, table_owner, table_name
  FROM user_synonyms
 ORDER BY synonym_name;
```

## 5. Documentação

- `COMMENT ON TABLE` e `COMMENT ON COLUMN` para toda tabela e coluna.
- Dicionário de dados consultado em `ALL_TAB_COMMENTS` e `ALL_COL_COMMENTS`.

## 6. Scripts de deploy e migrações

- Use nomes qualificados (`academico.alunos`) em scripts de deploy, para que o resultado não dependa da sessão de quem executa.
- Sinônimos ficam nos scripts de acesso de cada usuário, separados dos scripts de criação de tabelas.

## 7. Checklist antes de publicar um objeto

- [ ] Nome em snake_case minúsculo, completo, sem abreviação
- [ ] Tabela no plural, coluna no singular
- [ ] Sem aspas duplas em nomes de objetos
- [ ] Nome dentro do limite de bytes da versão do Oracle
- [ ] Restrições e índices com prefixo padrão
- [ ] Tipos `VARCHAR2`, `NUMBER`, `DATE` ou `TIMESTAMP` com tamanho definido
- [ ] CPF, CNPJ e documentos em `VARCHAR2(20)`
- [ ] `COMMENT ON` para tabela e colunas
- [ ] Sinônimo privado criado e `GRANT` emitido para o usuário
