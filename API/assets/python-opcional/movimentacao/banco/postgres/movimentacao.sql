-- =============================================================================
-- Movimentação e log de erros da API (PostgreSQL 15+), versão 2 (ULID, uma gravação por requisição)
-- ANTES de executar, troque <schema> (schema da aplicação, ex.: app) e <usuario_app> (o usuário da API).
-- Executar como dono do schema, no banco da aplicação. Pode ser executado de novo: o que existe é mantido.
--
-- Uma linha por requisição com chave de API válida (menos /health e /metrics): o que chegou e o que a API respondeu, gravada de
-- uma vez, em segundo plano, depois da resposta. A chave é o requisicao_id: um ULID de 26 caracteres gerado na API (o mesmo que o
-- cliente recebe em X-Requisicao). Não há contador nem função de numeração. O CPF no conteúdo é gravado mascarado
-- (***.999.999-**) pela própria API.
--
-- A aplicação (<usuario_app>) só INSERE em movimentacao e log_erros: não lê, não altera nem apaga.
-- Limpeza (como administrador), ex.: manter 90 dias, apagando primeiro o log de erros:
--   delete from <schema>.log_erros where ocorrido_em < now() - interval '90 days';
--   delete from <schema>.movimentacao where requisicao_em < now() - interval '90 days';
-- Com muito volume, particione movimentacao por requisicao_em (dia ou mês) e descarte as partições antigas.
--
-- MIGRAÇÃO da versão 1 (ano + contador): esta versão NÃO converte os dados. Exporte o que precisar e então remova os objetos antigos:
--   drop table if exists <schema>.log_erros, <schema>.movimentacao, <schema>.contador_requisicao;
--   drop function if exists <schema>.registrar_requisicao(varchar, varchar, text);
--   drop function if exists <schema>.registrar_resposta(smallint, bigint, smallint, text, char, char);
-- =============================================================================

create table if not exists <schema>.movimentacao (
    requisicao_id        varchar(26)   not null,
    metodo               varchar(10)   not null,
    rota                 varchar(200)  not null,
    requisicao_conteudo  text,
    requisicao_em        timestamptz   not null,
    situacao_requisicao  char(2)       not null,
    status_http          smallint      not null,
    resposta_conteudo    text,
    resposta_em          timestamptz   not null,
    situacao_resposta    char(2)       not null,

    constraint pk_movimentacao                     primary key (requisicao_id),
    constraint ck_movimentacao_situacao_requisicao check (situacao_requisicao in ('OK', 'ER')),
    constraint ck_movimentacao_situacao_resposta   check (situacao_resposta in ('OK', 'ER')),
    constraint ck_movimentacao_status_http         check (status_http between 100 and 599)
);

create index if not exists idx_movimentacao_requisicao_em on <schema>.movimentacao (requisicao_em);

comment on table  <schema>.movimentacao                     is 'Requisições recebidas pela API e as respostas dadas. CPF mascarado; sem a chave de API.';
comment on column <schema>.movimentacao.requisicao_id       is 'ULID da requisição (26 caracteres), gerado na API e devolvido ao cliente em X-Requisicao. Carrega a hora de chegada.';
comment on column <schema>.movimentacao.metodo              is 'Método HTTP (POST, GET...).';
comment on column <schema>.movimentacao.rota                is 'Caminho pedido, sem query string (truncado em 200 caracteres).';
comment on column <schema>.movimentacao.requisicao_conteudo is 'Corpo recebido, como texto, em qualquer formato (até 32 KB, CPF mascarado). Nulo se não houve corpo.';
comment on column <schema>.movimentacao.requisicao_em       is 'Momento em que a requisição chegou.';
comment on column <schema>.movimentacao.situacao_requisicao is 'OK = aceita (chave e entrada válidas); ER = recusada (respostas 4xx).';
comment on column <schema>.movimentacao.status_http         is 'Status HTTP da resposta.';
comment on column <schema>.movimentacao.resposta_conteudo   is 'Corpo da resposta, como texto (até 32 KB, CPF mascarado).';
comment on column <schema>.movimentacao.resposta_em         is 'Momento em que a API terminou a resposta.';
comment on column <schema>.movimentacao.situacao_resposta   is 'OK = sucesso (2xx); ER = erro (4xx ou 5xx).';

-- Log de erros: uma linha por resposta 4xx/5xx. Só metadados: NUNCA CPF, CNPJ, nome ou corpo da requisição.
-- É gravado na mesma transação da sua movimentação.
create table if not exists <schema>.log_erros (
    requisicao_id varchar(26)   not null,
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
    constraint fk_log_erros_movimentacao  foreign key (requisicao_id) references <schema>.movimentacao (requisicao_id),
    constraint ck_log_erros_status        check (status between 400 and 599),
    constraint ck_log_erros_duracao       check (duracao_ms >= 0)
);

create index if not exists idx_log_erros_ocorrido_em on <schema>.log_erros (ocorrido_em);
create index if not exists idx_log_erros_requisicao on <schema>.log_erros (requisicao_id);

comment on table  <schema>.log_erros              is 'Respostas de erro (4xx e 5xx) da API. Só metadados, sem dados pessoais.';
comment on column <schema>.log_erros.requisicao_id is 'ULID da requisição na movimentação (o mesmo do header X-Requisicao).';
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

-- A aplicação só insere: os privilégios padrão do schema dariam select/update/delete, retirados aqui.
revoke all on <schema>.movimentacao, <schema>.log_erros from <usuario_app>;
grant insert on <schema>.movimentacao, <schema>.log_erros to <usuario_app>;
