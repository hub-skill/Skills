-- =============================================================================
-- Movimentação e log de erros da API (PostgreSQL 15+)
-- ANTES de executar, troque <schema> (schema da aplicação, ex.: app) e <usuario_app> (o usuário da API).
-- Executar como dono do schema, no banco da aplicação. Pode ser executado de novo: o que existe é mantido.
--
-- Uma linha por requisição com chave de API válida (menos /health e /metrics): o que chegou e o que a API
-- respondeu. Chave: (ano, requisicao); o contador volta a 1 em todo ano novo. CPF no conteúdo é gravado
-- mascarado (***.999.999-**) pela própria API.
--
-- A aplicação (<usuario_app>) não tem privilégio nas tabelas de movimentação: só executa as duas funções abaixo
-- (SECURITY DEFINER), que numeram, gravam e completam a linha uma única vez. Na log_erros ela só INSERE.
-- Limpeza (como administrador), ex.: manter 90 dias, apagando primeiro o log de erros:
--   delete from <schema>.log_erros where ocorrido_em < now() - interval '90 days';
--   delete from <schema>.movimentacao where requisicao_em < now() - interval '90 days';
-- =============================================================================

-- -----------------------------------------------------------------------------
-- Contador: último número usado em cada ano
-- -----------------------------------------------------------------------------
create table if not exists <schema>.contador_requisicao (
    ano     smallint  not null,
    ultimo  bigint    not null,

    constraint pk_contador_requisicao         primary key (ano),
    constraint ck_contador_requisicao_ultimo  check (ultimo >= 1)
);

comment on table  <schema>.contador_requisicao        is 'Último número de requisição usado em cada ano (a numeração da movimentação volta a 1 em todo ano novo).';
comment on column <schema>.contador_requisicao.ano    is 'Ano (horário de Brasília).';
comment on column <schema>.contador_requisicao.ultimo is 'Último número entregue neste ano.';

-- -----------------------------------------------------------------------------
-- Movimentação: requisição e resposta
-- -----------------------------------------------------------------------------
create table if not exists <schema>.movimentacao (
    ano                  smallint      not null,
    requisicao           bigint        not null,
    metodo               varchar(10)   not null,
    rota                 varchar(200)  not null,
    requisicao_conteudo  text,
    requisicao_em        timestamptz   not null default now(),
    situacao_requisicao  char(2),
    status_http          smallint,
    resposta_conteudo    text,
    resposta_em          timestamptz,
    situacao_resposta    char(2),

    constraint pk_movimentacao                      primary key (ano, requisicao),
    constraint ck_movimentacao_situacao_requisicao  check (situacao_requisicao in ('OK', 'ER')),
    constraint ck_movimentacao_situacao_resposta    check (situacao_resposta in ('OK', 'ER')),
    constraint ck_movimentacao_status_http          check (status_http between 100 and 599)
);

create index if not exists idx_movimentacao_requisicao_em on <schema>.movimentacao (requisicao_em);

comment on table  <schema>.movimentacao                     is 'Requisições recebidas pela API Python e as respostas dadas. CPF mascarado; sem a chave de API.';
comment on column <schema>.movimentacao.ano                 is 'Ano da requisição (horário de Brasília). Chave junto com requisicao.';
comment on column <schema>.movimentacao.requisicao          is 'Número da requisição no ano (começa em 1 a cada ano). Devolvido ao cliente no header X-Requisicao.';
comment on column <schema>.movimentacao.metodo              is 'Método HTTP (POST, GET...).';
comment on column <schema>.movimentacao.rota                is 'Caminho pedido, sem query string (truncado em 200 caracteres).';
comment on column <schema>.movimentacao.requisicao_conteudo is 'Corpo recebido, como texto, em qualquer formato (até 32 KB, CPF mascarado). Nulo se não houve corpo.';
comment on column <schema>.movimentacao.requisicao_em       is 'Momento em que a requisição chegou.';
comment on column <schema>.movimentacao.situacao_requisicao is 'OK = aceita (chave e entrada válidas); ER = recusada (respostas 4xx). Nulo enquanto não houver resposta.';
comment on column <schema>.movimentacao.status_http         is 'Status HTTP da resposta.';
comment on column <schema>.movimentacao.resposta_conteudo   is 'Corpo da resposta, como texto (até 32 KB, CPF mascarado).';
comment on column <schema>.movimentacao.resposta_em         is 'Momento da resposta. Nulo = a API não chegou a responder (ex.: caiu no meio).';
comment on column <schema>.movimentacao.situacao_resposta   is 'OK = sucesso (2xx); ER = erro (4xx ou 5xx).';

-- -----------------------------------------------------------------------------
-- Funções usadas pela aplicação
-- -----------------------------------------------------------------------------
-- Numera e grava a chegada num único comando: duas requisições ao mesmo tempo nunca recebem o mesmo
-- número (a linha do ano no contador fica travada até o fim da transação, que é curta).
create or replace function <schema>.registrar_requisicao(p_metodo varchar, p_rota varchar, p_conteudo text)
returns table (ano smallint, requisicao bigint)
language sql
security definer
set search_path = <schema>, pg_temp
as $$
    with numero as (
        insert into <schema>.contador_requisicao as c (ano, ultimo)
        values (extract(year from now() at time zone 'America/Sao_Paulo')::smallint, 1)
        on conflict on constraint pk_contador_requisicao do update set ultimo = c.ultimo + 1
        returning c.ano, c.ultimo
    )
    insert into <schema>.movimentacao (ano, requisicao, metodo, rota, requisicao_conteudo)
    select numero.ano, numero.ultimo, left(p_metodo, 10), left(p_rota, 200), p_conteudo
      from numero
    returning movimentacao.ano, movimentacao.requisicao;
$$;

-- Completa a linha com a resposta, uma única vez (não sobrescreve uma resposta já gravada).
create or replace function <schema>.registrar_resposta(
    p_ano smallint, p_requisicao bigint, p_status_http smallint, p_conteudo text,
    p_situacao_requisicao char, p_situacao_resposta char)
returns void
language sql
security definer
set search_path = <schema>, pg_temp
as $$
    update <schema>.movimentacao
       set status_http         = p_status_http,
           resposta_conteudo   = p_conteudo,
           resposta_em         = now(),
           situacao_requisicao = p_situacao_requisicao,
           situacao_resposta   = p_situacao_resposta
     where ano = p_ano
       and requisicao = p_requisicao
       and resposta_em is null;
$$;

comment on function <schema>.registrar_requisicao(varchar, varchar, text) is 'Numera a requisição no ano e grava a chegada. Devolve (ano, requisicao).';
comment on function <schema>.registrar_resposta(smallint, bigint, smallint, text, char, char) is 'Completa a movimentação com a resposta (uma vez só).';

-- A aplicação só executa as funções: não lê, não altera nem apaga as tabelas diretamente.
revoke all on <schema>.contador_requisicao, <schema>.movimentacao from <usuario_app>;
revoke all on function <schema>.registrar_requisicao(varchar, varchar, text) from public;
revoke all on function <schema>.registrar_resposta(smallint, bigint, smallint, text, char, char) from public;
grant execute on function <schema>.registrar_requisicao(varchar, varchar, text) to <usuario_app>;
grant execute on function <schema>.registrar_resposta(smallint, bigint, smallint, text, char, char) to <usuario_app>;

-- -----------------------------------------------------------------------------
-- Log de erros: uma linha por resposta 4xx/5xx. Só metadados: NUNCA CPF, CNPJ, nome ou corpo da requisição.
-- (ano, requisicao) são as primeiras colunas, obrigatórias, e apontam para a movimentação do erro.
-- -----------------------------------------------------------------------------
create table if not exists <schema>.log_erros (
    ano           smallint      not null,
    requisicao    bigint        not null,
    id            bigint        generated always as identity,
    ocorrido_em   timestamptz   not null default now(),
    request_id    varchar(64)   not null,
    metodo        varchar(10)   not null,
    rota          varchar(200)  not null,
    status        smallint      not null,
    codigo        varchar(40)   not null,
    titulo        varchar(200)  not null,
    tipo_excecao  varchar(200),
    duracao_ms    integer,
    ambiente      varchar(20)   not null,

    constraint pk_log_erros               primary key (id),
    constraint fk_log_erros_movimentacao  foreign key (ano, requisicao) references <schema>.movimentacao (ano, requisicao),
    constraint ck_log_erros_status        check (status between 400 and 599),
    constraint ck_log_erros_duracao       check (duracao_ms >= 0)
);

create index if not exists idx_log_erros_ocorrido_em on <schema>.log_erros (ocorrido_em);
create index if not exists idx_log_erros_requisicao on <schema>.log_erros (ano, requisicao);

comment on table  <schema>.log_erros              is 'Respostas de erro (4xx e 5xx) da API. Só metadados, sem dados pessoais.';
comment on column <schema>.log_erros.ano          is 'Ano da requisição na movimentação (chave junto com requisicao).';
comment on column <schema>.log_erros.requisicao   is 'Número da requisição na movimentação (o mesmo do header X-Requisicao).';
comment on column <schema>.log_erros.id           is 'Identificador interno.';
comment on column <schema>.log_erros.ocorrido_em  is 'Momento da gravação (alguns milissegundos depois da resposta).';
comment on column <schema>.log_erros.request_id   is 'Mesmo request_id devolvido ao cliente no corpo do erro e no header X-Request-ID.';
comment on column <schema>.log_erros.metodo       is 'Método HTTP (POST, GET...).';
comment on column <schema>.log_erros.rota         is 'Caminho pedido, sem query string (truncado em 200 caracteres).';
comment on column <schema>.log_erros.status       is 'Status HTTP da resposta (400 a 599).';
comment on column <schema>.log_erros.codigo       is 'Campo code do erro (ex.: entrada_invalida, banco_indisponivel).';
comment on column <schema>.log_erros.titulo       is 'Campo title do erro.';
comment on column <schema>.log_erros.tipo_excecao is 'Tipo da exceção, só em erro_interno (500). O stack trace fica no log da aplicação.';
comment on column <schema>.log_erros.duracao_ms   is 'Tempo da requisição até o erro, em milissegundos.';
comment on column <schema>.log_erros.ambiente     is 'Ambiente da API (dev, staging, prod).';

-- A aplicação só insere. Os privilégios padrão do schema dariam select/update/delete: retirados aqui.
revoke all on <schema>.log_erros from <usuario_app>;
grant insert on <schema>.log_erros to <usuario_app>;
