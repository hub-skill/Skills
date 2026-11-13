# Módulo opcional: movimentação e log de erros no banco (Python), versão 2 (ULID)

Use quando a API precisa **guardar o que recebeu e o que respondeu** (auditoria, suporte, "o cliente diz que mandou X").
Exige o módulo `bancos` (PostgreSQL e/ou Oracle). **Pergunte antes de incluir**: grava dados da entrada, tem custo e exige retenção
(veja "Quando usar"). Gêmeo do módulo .NET, com o mesmo comportamento e os mesmos SQL.

## O que faz

- Toda requisição **com chave de API válida** (menos `/health/*` e `/metrics`) ganha um **ULID** de 26 caracteres, gerado na própria API (sem
  banco), devolvido ao cliente no header `X-Requisicao` (ex.: `01JC4M7Z9Q8T2X5V6B3N0WYKHD`) para informar ao suporte. O ULID carrega a hora de
  chegada, é único na prática e, em ordem alfabética, é a ordem cronológica. **Não é sequencial e não reinicia a cada ano.**
- Depois da resposta, **uma só gravação**, em segundo plano: o que chegou, o que a API respondeu e, se foi erro (4xx/5xx), os metadados do erro
  (tabela `log_erros`), tudo na mesma transação. O banco nunca está no caminho da resposta.
- Situação da requisição: `OK` = aceita; `ER` = recusada (4xx). Situação da resposta: `OK` = 2xx; `ER` = 4xx e 5xx.
- `log_erros`: só metadados (nunca documento, nome nem corpo). Chamadas **sem chave válida** recebem 401 e **não gravam nada**.

## O que traz

| Arquivo | Conteúdo |
|---|---|
| `banco/postgres/movimentacao.sql`, `banco/oracle/movimentacao.sql` | Tabelas `movimentacao` (chave `requisicao_id`) e `log_erros` (com chave estrangeira); instantes em `timestamptz` / `TIMESTAMP WITH TIME ZONE`; a aplicação só tem `INSERT`. Traz a migração da versão 1 (ano + contador) |
| `app/movimentacao/ulid.py` | Gerador (`secrets`), validação e leitura da hora do ULID |
| `app/movimentacao/regras.py` | Máscara de CPF, limite de 32 KB, situações, rotas sem movimentação |
| `app/movimentacao/gravacao.py` | Fila em memória (1000) e gravação em lotes (100), em segundo plano; falha do banco só vira aviso |
| `app/movimentacao/registros.py` | `RegistroMovimentacao`, `RegistroErro` e o contrato do repositório (`gravar`) |
| `app/movimentacao/postgres.py`, `oracle.py` | Repositórios: um lote por transação (movimentação e erro juntos); traduzem falhas do banco |
| `app/movimentacao/http.py` | `Movimentacao`: gera o ULID na chegada, enfileira a gravação, devolve `X-Requisicao`, guarda os metadados do erro |
| `tests/unit/test_movimentacao.py`, `tests/integration/test_movimentacao_api.py`, `tests/falhas/` | Regras, ULID e fila; a API com repositório falso; o banco fora do ar (porta fechada, roda no CI) |

Sem Oracle, apague `oracle.py` e o teste de Oracle; sem PostgreSQL, o inverso.

## Como incluir

1. **Banco.** Troque `<schema>` (PostgreSQL), `<dono>` (Oracle, schema dono das tabelas, sem login) e `<usuario_app>` (o usuário da API) em
   `banco/<banco>/movimentacao.sql` e execute como administrador. O schema precisa existir e o usuário da API precisa de `USAGE` nele
   (PostgreSQL) ou `CREATE SESSION` (Oracle). Se já existe a **versão 1**, remova os objetos antigos (comandos no cabeçalho do SQL): a versão 2 não
   converte dados.
2. **Arquivos.** Copie `app/movimentacao/` para `app/movimentacao/` da API e os testes para `tests/` (o `tests/falhas/` traz o hook
   `pytest_asyncio_loop_factories`, necessário no Windows com psycopg async).
3. **`main.py`.** Quatro mudanças:

```python
# (a) imports
from app.movimentacao.http import Movimentacao, registrar_erro
from app.movimentacao.regras import conteudo_da_requisicao, movimentar

# (b) _resposta_erro: guarda os metadados do erro (não faz nada se a movimentação não estiver ligada).
#     Acrescente o parâmetro tipo_excecao e passe-o no handler do 500:
#     tipo_excecao=f"{type(exc).__module__}.{type(exc).__qualname__}"
def _resposta_erro(request, status_code, codigo, mensagem, headers=None, tipo_excecao=None):
    registrar_erro(request, status_code, codigo, mensagem, tipo_excecao)
    ...

# (c) no middleware contexto_da_requisicao, depois de calcular `corpo` e antes do `if grande:`
    movimentacao = Movimentacao.da(request) if chave_ok and movimentar(request.url.path) else None
    if movimentacao:
        movimentacao.abrir(request, conteudo_da_requisicao(corpo, tamanho, grande))
#     e, antes de acrescentar os headers de segurança à resposta:
    if movimentacao:
        response = await movimentacao.completar(request, response)

# (d) lifespan: cria a movimentação com o repositório do seu banco e grava o que sobrar ao desligar
@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncIterator[None]:
    repositorio = MovimentacaoPostgres(pool, "app")  # ou MovimentacaoOracle(pool_oracle), com o pool já criado
    app.state.movimentacao = Movimentacao(repositorio, settings.app_env)
    app.state.movimentacao.iniciar()
    try:
        yield
    finally:
        await app.state.movimentacao.parar()  # grava o que ainda estiver na fila antes de fechar o pool
```

   O repositório recebe o **pool** que a API já usa (módulo `bancos`, item 5 do LEIAME dele). Passe `lifespan=lifespan` ao `FastAPI(...)`.
4. Rode o Passo 5 (ruff, mypy, pytest). O teste `test_movimentacao_falhas.py` usa os drivers reais contra uma porta sem servidor.

## Regras que valem (mantenha ao adaptar)

- **O número da requisição nasce na API**: nunca depende do banco. Com o banco fora ou lento, a resposta sai normalmente com `X-Requisicao`; a fila
  enche e as gravações a mais se perdem (a API avisa no log). A gravação nunca derruba a API.
- **Conteúdo**: texto em qualquer formato, até 32 KB; corpo acima do limite da API só grava o tamanho. **Todo CPF é mascarado** (`***.982.247-**`) na
  requisição e na resposta. A chave de API vai num header e nunca é gravada. Se a API recebe outros dados pessoais (nome, e-mail, telefone...), eles
  são gravados como vieram: decida se mascara, **defina o prazo de retenção** (limpeza no cabeçalho do SQL) e restrinja o acesso às tabelas.
- **A chegada não é gravada antes do processamento**: se o processo cair no meio da requisição, ela só existe no log da aplicação.
- **Banco de leitura**: a sessão da API continua somente leitura; só a transação da gravação pede escrita (`set transaction read write`), com SQL fixo.
- **`DOCUMENTACAO.md`**: documente o header `X-Requisicao` (ULID), as tabelas e o aviso de privacidade acima.
- **Postman**: no pedido de sucesso da coleção (`postman/`), acrescente o teste `pm.test("X-Requisicao é um ULID (26 caracteres)", () => pm.expect(pm.response.headers.get("X-Requisicao")).to.match(/^[0-9A-HJKMNP-TV-Z]{26}$/));`. A coleção base não o traz porque o header só existe com este módulo.

## Quando usar (e quando não)

| Situação | Recomendação |
|---|---|
| Consulta a dados **públicos** com entrada pública (ex.: CNPJ no CEPIM) | Não incluir: o log estruturado com `request_id` basta |
| Decisão sobre **pessoa física** (CPF, nome) com exigência de auditoria ou explicação (LGPD, art. 20) | Incluir, com dono, retenção definida e finalidade registrada |
| Volume de **milhares por segundo** | Não gravar corpo no banco relacional: só metadados, amostra do corpo, ou um pipeline de logs; particione por `requisicao_em` |

## Garantias do ULID

- **Único**: 80 bits aleatórios por milissegundo. Mesmo com 20.000 requisições por segundo, a chance de repetir é da ordem de 5 em 10¹² por ano;
  a chave primária é a garantia final (o lote que repetir é recusado inteiro).
- **Ordenado pela hora**, não **sequencial**: não há "requisição nº 123 do ano" nem como detectar requisição faltando pela numeração. Se isso for
  exigência, este módulo não serve (use um contador no banco, com o custo medido de -29% a -34% de vazão na versão 1) ou um hash encadeado.
- O gerador usa `secrets` (criptográfico). Não troque por `random`. Hora com 48 bits: vale até o ano 10889.
