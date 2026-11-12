-- =============================================================================
-- Movimentação e log de erros da API (Oracle 21c)
-- ANTES de executar, troque <dono> (schema dono das tabelas, sem login) e <usuario_app> (o usuário da API).
-- Executar como DBA no banco plugável. Cada comando termina numa linha com "/". Reexecutar não dá problema.
--
-- Uma linha por requisição com chave de API válida (menos /health e /metrics): o que chegou e o que a API
-- respondeu. Chave: (ano, requisicao); o contador volta a 1 em todo ano novo. CPF no conteúdo é gravado
-- mascarado (***.999.999-**) pela própria API.
--
-- A aplicação (<usuario_app>) não tem privilégio nas tabelas de movimentação: só executa as duas procedures
-- abaixo (direitos do dono), pelos sinônimos. Na log_erros ela só INSERE.
-- Limpeza (como administrador), ex.: manter 90 dias, apagando primeiro o log de erros:
--   DELETE FROM <dono>.log_erros WHERE ocorrido_em < SYSTIMESTAMP - INTERVAL '90' DAY;
--   DELETE FROM <dono>.movimentacao WHERE requisicao_em < SYSTIMESTAMP - INTERVAL '90' DAY;
-- =============================================================================

CREATE TABLE <dono>.contador_requisicao (
    ano     NUMBER(4)   NOT NULL,
    ultimo  NUMBER(12)  NOT NULL,

    CONSTRAINT pk_contador_requisicao        PRIMARY KEY (ano),
    CONSTRAINT ck_contador_requisicao_ultimo CHECK (ultimo >= 1)
)
/
COMMENT ON TABLE <dono>.contador_requisicao IS 'Último número de requisição usado em cada ano (a numeração da movimentação volta a 1 em todo ano novo).'
/
COMMENT ON COLUMN <dono>.contador_requisicao.ano IS 'Ano (horário de Brasília).'
/
COMMENT ON COLUMN <dono>.contador_requisicao.ultimo IS 'Último número entregue neste ano.'
/
CREATE TABLE <dono>.movimentacao (
    ano                  NUMBER(4)      NOT NULL,
    requisicao           NUMBER(12)     NOT NULL,
    metodo               VARCHAR2(10)   NOT NULL,
    rota                 VARCHAR2(200)  NOT NULL,
    requisicao_conteudo  CLOB,
    requisicao_em        TIMESTAMP WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL,
    situacao_requisicao  CHAR(2),
    status_http          NUMBER(3),
    resposta_conteudo    CLOB,
    resposta_em          TIMESTAMP WITH TIME ZONE,
    situacao_resposta    CHAR(2),

    CONSTRAINT pk_movimentacao                     PRIMARY KEY (ano, requisicao),
    CONSTRAINT ck_movimentacao_situacao_requisicao CHECK (situacao_requisicao IN ('OK', 'ER')),
    CONSTRAINT ck_movimentacao_situacao_resposta   CHECK (situacao_resposta IN ('OK', 'ER')),
    CONSTRAINT ck_movimentacao_status_http         CHECK (status_http BETWEEN 100 AND 599)
)
/
CREATE INDEX <dono>.idx_movimentacao_requisicao_em ON <dono>.movimentacao (requisicao_em)
/
COMMENT ON TABLE <dono>.movimentacao IS 'Requisições recebidas pela API Python e as respostas dadas. CPF mascarado; sem a chave de API.'
/
COMMENT ON COLUMN <dono>.movimentacao.ano IS 'Ano da requisição (horário de Brasília). Chave junto com requisicao.'
/
COMMENT ON COLUMN <dono>.movimentacao.requisicao IS 'Número da requisição no ano (começa em 1 a cada ano). Devolvido ao cliente no header X-Requisicao.'
/
COMMENT ON COLUMN <dono>.movimentacao.metodo IS 'Método HTTP (POST, GET...).'
/
COMMENT ON COLUMN <dono>.movimentacao.rota IS 'Caminho pedido, sem query string (truncado em 200 caracteres).'
/
COMMENT ON COLUMN <dono>.movimentacao.requisicao_conteudo IS 'Corpo recebido, como texto, em qualquer formato (até 32 KB, CPF mascarado). Nulo se não houve corpo.'
/
COMMENT ON COLUMN <dono>.movimentacao.requisicao_em IS 'Momento em que a requisição chegou.'
/
COMMENT ON COLUMN <dono>.movimentacao.situacao_requisicao IS 'OK = aceita (chave e entrada válidas); ER = recusada (respostas 4xx). Nulo enquanto não houver resposta.'
/
COMMENT ON COLUMN <dono>.movimentacao.status_http IS 'Status HTTP da resposta.'
/
COMMENT ON COLUMN <dono>.movimentacao.resposta_conteudo IS 'Corpo da resposta, como texto (até 32 KB, CPF mascarado).'
/
COMMENT ON COLUMN <dono>.movimentacao.resposta_em IS 'Momento da resposta. Nulo = a API não chegou a responder (ex.: caiu no meio).'
/
COMMENT ON COLUMN <dono>.movimentacao.situacao_resposta IS 'OK = sucesso (2xx); ER = erro (4xx ou 5xx).'
/
-- Numera e grava a chegada. A linha do ano no contador fica travada até o fim da transação (curta):
-- duas requisições ao mesmo tempo nunca recebem o mesmo número.
CREATE OR REPLACE PROCEDURE <dono>.registrar_requisicao (
    p_metodo     IN  VARCHAR2,
    p_rota       IN  VARCHAR2,
    p_conteudo   IN  CLOB,
    p_ano        OUT NUMBER,
    p_requisicao OUT NUMBER
) AUTHID DEFINER AS
BEGIN
    p_ano := EXTRACT(YEAR FROM (SYSTIMESTAMP AT TIME ZONE 'America/Sao_Paulo'));
    UPDATE contador_requisicao SET ultimo = ultimo + 1 WHERE ano = p_ano RETURNING ultimo INTO p_requisicao;
    IF SQL%ROWCOUNT = 0 THEN
        BEGIN
            INSERT INTO contador_requisicao (ano, ultimo) VALUES (p_ano, 1);
            p_requisicao := 1;
        EXCEPTION
            WHEN DUP_VAL_ON_INDEX THEN -- outra requisição abriu o ano ao mesmo tempo
                UPDATE contador_requisicao SET ultimo = ultimo + 1 WHERE ano = p_ano RETURNING ultimo INTO p_requisicao;
        END;
    END IF;
    INSERT INTO movimentacao (ano, requisicao, metodo, rota, requisicao_conteudo)
    VALUES (p_ano, p_requisicao, SUBSTR(p_metodo, 1, 10), SUBSTR(p_rota, 1, 200), p_conteudo);
END;
/
-- Completa a linha com a resposta, uma única vez (não sobrescreve uma resposta já gravada).
CREATE OR REPLACE PROCEDURE <dono>.registrar_resposta (
    p_ano                 IN NUMBER,
    p_requisicao          IN NUMBER,
    p_status_http         IN NUMBER,
    p_conteudo            IN CLOB,
    p_situacao_requisicao IN VARCHAR2,
    p_situacao_resposta   IN VARCHAR2
) AUTHID DEFINER AS
BEGIN
    UPDATE movimentacao
       SET status_http         = p_status_http,
           resposta_conteudo   = p_conteudo,
           resposta_em         = SYSTIMESTAMP,
           situacao_requisicao = p_situacao_requisicao,
           situacao_resposta   = p_situacao_resposta
     WHERE ano = p_ano
       AND requisicao = p_requisicao
       AND resposta_em IS NULL;
END;
/
-- A aplicação só executa as procedures (pelos sinônimos): não lê, não altera nem apaga as tabelas.
GRANT EXECUTE ON <dono>.registrar_requisicao TO <usuario_app>
/
GRANT EXECUTE ON <dono>.registrar_resposta TO <usuario_app>
/
CREATE OR REPLACE SYNONYM <usuario_app>.registrar_requisicao FOR <dono>.registrar_requisicao
/
CREATE OR REPLACE SYNONYM <usuario_app>.registrar_resposta FOR <dono>.registrar_resposta
/
CREATE TABLE <dono>.log_erros (
    ano           NUMBER(4)      NOT NULL,
    requisicao    NUMBER(12)     NOT NULL,
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
    CONSTRAINT fk_log_erros_movimentacao FOREIGN KEY (ano, requisicao) REFERENCES <dono>.movimentacao (ano, requisicao),
    CONSTRAINT ck_log_erros_status       CHECK (status BETWEEN 400 AND 599),
    CONSTRAINT ck_log_erros_duracao      CHECK (duracao_ms >= 0)
)
/
CREATE INDEX <dono>.idx_log_erros_ocorrido_em ON <dono>.log_erros (ocorrido_em)
/
CREATE INDEX <dono>.idx_log_erros_requisicao ON <dono>.log_erros (ano, requisicao)
/
COMMENT ON TABLE <dono>.log_erros IS 'Respostas de erro (4xx e 5xx) da API. Só metadados, sem dados pessoais.'
/
COMMENT ON COLUMN <dono>.log_erros.ano IS 'Ano da requisição na movimentação (chave junto com requisicao).'
/
COMMENT ON COLUMN <dono>.log_erros.requisicao IS 'Número da requisição na movimentação (o mesmo do header X-Requisicao).'
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
-- A aplicação só insere (sem SELECT, UPDATE ou DELETE) e enxerga a tabela pelo sinônimo.
GRANT INSERT ON <dono>.log_erros TO <usuario_app>
/
CREATE OR REPLACE SYNONYM <usuario_app>.log_erros FOR <dono>.log_erros
/
