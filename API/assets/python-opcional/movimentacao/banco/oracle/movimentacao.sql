-- =============================================================================
-- Movimentação e log de erros da API (Oracle 21c), versão 2 (ULID, uma gravação por requisição)
-- ANTES de executar, troque <dono> (schema dono das tabelas, sem login) e <usuario_app> (o usuário da API).
-- Executar como DBA no banco plugável. Cada comando termina numa linha com "/". Reexecutar não dá problema.
--
-- Uma linha por requisição com chave de API válida (menos /health e /metrics): o que chegou e o que a API respondeu, gravada de
-- uma vez, em segundo plano, depois da resposta. A chave é o requisicao_id: um ULID de 26 caracteres gerado na API (o mesmo que o
-- cliente recebe em X-Requisicao). Não há contador nem procedure de numeração. O CPF no conteúdo é gravado mascarado
-- (***.999.999-**) pela própria API.
--
-- A aplicação (<usuario_app>) só INSERE em movimentacao e log_erros (pelos sinônimos): não lê, não altera nem apaga.
-- Limpeza (como administrador), ex.: manter 90 dias, apagando primeiro o log de erros:
--   DELETE FROM <dono>.log_erros WHERE ocorrido_em < SYSTIMESTAMP - INTERVAL '90' DAY;
--   DELETE FROM <dono>.movimentacao WHERE requisicao_em < SYSTIMESTAMP - INTERVAL '90' DAY;
-- Com muito volume, particione movimentacao por requisicao_em e descarte as partições antigas.
--
-- MIGRAÇÃO da versão 1 (ano + contador): esta versão NÃO converte os dados. Exporte o que precisar e então remova os objetos antigos:
--   DROP TABLE <dono>.log_erros;  DROP TABLE <dono>.movimentacao;  DROP TABLE <dono>.contador_requisicao;
--   DROP PROCEDURE <dono>.registrar_requisicao;  DROP PROCEDURE <dono>.registrar_resposta;
--   (e os sinônimos antigos de <usuario_app>: registrar_requisicao, registrar_resposta)
-- =============================================================================

CREATE TABLE <dono>.movimentacao (
    requisicao_id        VARCHAR2(26)   NOT NULL,
    metodo               VARCHAR2(10)   NOT NULL,
    rota                 VARCHAR2(200)  NOT NULL,
    requisicao_conteudo  CLOB,
    requisicao_em        TIMESTAMP WITH TIME ZONE NOT NULL,
    situacao_requisicao  CHAR(2)        NOT NULL,
    status_http          NUMBER(3)      NOT NULL,
    resposta_conteudo    CLOB,
    resposta_em          TIMESTAMP WITH TIME ZONE NOT NULL,
    situacao_resposta    CHAR(2)        NOT NULL,

    CONSTRAINT pk_movimentacao                     PRIMARY KEY (requisicao_id),
    CONSTRAINT ck_movimentacao_situacao_requisicao CHECK (situacao_requisicao IN ('OK', 'ER')),
    CONSTRAINT ck_movimentacao_situacao_resposta   CHECK (situacao_resposta IN ('OK', 'ER')),
    CONSTRAINT ck_movimentacao_status_http         CHECK (status_http BETWEEN 100 AND 599)
)
/
CREATE INDEX <dono>.idx_movimentacao_requisicao_em ON <dono>.movimentacao (requisicao_em)
/
COMMENT ON TABLE <dono>.movimentacao IS 'Requisições recebidas pela API e as respostas dadas. CPF mascarado; sem a chave de API.'
/
COMMENT ON COLUMN <dono>.movimentacao.requisicao_id IS 'ULID da requisição (26 caracteres), gerado na API e devolvido ao cliente em X-Requisicao. Carrega a hora de chegada.'
/
COMMENT ON COLUMN <dono>.movimentacao.metodo IS 'Método HTTP (POST, GET...).'
/
COMMENT ON COLUMN <dono>.movimentacao.rota IS 'Caminho pedido, sem query string (truncado em 200 caracteres).'
/
COMMENT ON COLUMN <dono>.movimentacao.requisicao_conteudo IS 'Corpo recebido, como texto, em qualquer formato (até 32 KB, CPF mascarado). Nulo se não houve corpo.'
/
COMMENT ON COLUMN <dono>.movimentacao.requisicao_em IS 'Momento em que a requisição chegou.'
/
COMMENT ON COLUMN <dono>.movimentacao.situacao_requisicao IS 'OK = aceita (chave e entrada válidas); ER = recusada (respostas 4xx).'
/
COMMENT ON COLUMN <dono>.movimentacao.status_http IS 'Status HTTP da resposta.'
/
COMMENT ON COLUMN <dono>.movimentacao.resposta_conteudo IS 'Corpo da resposta, como texto (até 32 KB, CPF mascarado).'
/
COMMENT ON COLUMN <dono>.movimentacao.resposta_em IS 'Momento em que a API terminou a resposta.'
/
COMMENT ON COLUMN <dono>.movimentacao.situacao_resposta IS 'OK = sucesso (2xx); ER = erro (4xx ou 5xx).'
/
CREATE TABLE <dono>.log_erros (
    requisicao_id VARCHAR2(26)   NOT NULL,
    id            NUMBER GENERATED ALWAYS AS IDENTITY,
    ocorrido_em   TIMESTAMP WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL,
    request_id    VARCHAR2(64)   NOT NULL,
    metodo        VARCHAR2(10)   NOT NULL,
    rota          VARCHAR2(200)  NOT NULL,
    status        NUMBER(3)      NOT NULL,
    codigo        VARCHAR2(40)   NOT NULL,
    titulo        VARCHAR2(200)  NOT NULL,
    tipo_excecao  VARCHAR2(200),
    duracao_ms    NUMBER(10),
    ambiente      VARCHAR2(20)   NOT NULL,

    CONSTRAINT pk_log_erros              PRIMARY KEY (id),
    CONSTRAINT fk_log_erros_movimentacao FOREIGN KEY (requisicao_id) REFERENCES <dono>.movimentacao (requisicao_id),
    CONSTRAINT ck_log_erros_status       CHECK (status BETWEEN 400 AND 599),
    CONSTRAINT ck_log_erros_duracao      CHECK (duracao_ms >= 0)
)
/
CREATE INDEX <dono>.idx_log_erros_ocorrido_em ON <dono>.log_erros (ocorrido_em)
/
CREATE INDEX <dono>.idx_log_erros_requisicao ON <dono>.log_erros (requisicao_id)
/
COMMENT ON TABLE <dono>.log_erros IS 'Respostas de erro (4xx e 5xx) da API. Só metadados, sem dados pessoais. Gravado na mesma transação da sua movimentação.'
/
COMMENT ON COLUMN <dono>.log_erros.requisicao_id IS 'ULID da requisição na movimentação (o mesmo do header X-Requisicao).'
/
COMMENT ON COLUMN <dono>.log_erros.id IS 'Identificador interno.'
/
COMMENT ON COLUMN <dono>.log_erros.ocorrido_em IS 'Momento da gravação (alguns milissegundos depois da resposta).'
/
COMMENT ON COLUMN <dono>.log_erros.request_id IS 'Mesmo request_id devolvido ao cliente no corpo do erro e no header X-Request-ID.'
/
COMMENT ON COLUMN <dono>.log_erros.metodo IS 'Método HTTP (POST, GET...).'
/
COMMENT ON COLUMN <dono>.log_erros.rota IS 'Caminho pedido, sem query string (truncado em 200 caracteres).'
/
COMMENT ON COLUMN <dono>.log_erros.status IS 'Status HTTP da resposta (400 a 599).'
/
COMMENT ON COLUMN <dono>.log_erros.codigo IS 'Campo code do erro (ex.: entrada_invalida, banco_indisponivel).'
/
COMMENT ON COLUMN <dono>.log_erros.titulo IS 'Campo title do erro.'
/
COMMENT ON COLUMN <dono>.log_erros.tipo_excecao IS 'Tipo da exceção, só em erro_interno (500). O stack trace fica no log da aplicação.'
/
COMMENT ON COLUMN <dono>.log_erros.duracao_ms IS 'Tempo da requisição até o erro, em milissegundos.'
/
COMMENT ON COLUMN <dono>.log_erros.ambiente IS 'Ambiente da API (dev, staging, prod).'
/
-- A aplicação só insere (sem SELECT, UPDATE ou DELETE) e enxerga as tabelas pelos sinônimos.
GRANT INSERT ON <dono>.movimentacao TO <usuario_app>
/
GRANT INSERT ON <dono>.log_erros TO <usuario_app>
/
CREATE OR REPLACE SYNONYM <usuario_app>.movimentacao FOR <dono>.movimentacao
/
CREATE OR REPLACE SYNONYM <usuario_app>.log_erros FOR <dono>.log_erros
/
