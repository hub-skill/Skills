# Módulo opcional: movimentação e log de erros no banco (Python)

Use quando a API precisa **guardar o que recebeu e o que respondeu** (auditoria, suporte, "o cliente diz que mandou X").
Exige o módulo `bancos` (PostgreSQL e/ou Oracle). Validado nos Motores de Crédito (Python e .NET) com os dois bancos reais.

## O que faz

- Toda requisição **com chave de API válida** (menos `/health/*` e `/metrics`) ganha um número (`ano` + `requisicao`; o contador
  **volta a 1 todo ano**) e é gravada na tabela `movimentacao`: o que chegou, em qualquer formato, e depois, na mesma linha, o que a
  API respondeu. O cliente recebe o número no header `X-Requisicao` (ex.: `2026-000123`) para informar ao suporte.
- Cada linha tem a situação da requisição (`OK` = aceita; `ER` = recusada, 4xx) e a da resposta (`OK` = 2xx; `ER` = 4xx e 5xx).
- Toda resposta de erro (4xx e 5xx) vai também para `log_erros`, com a chave `ano` + `requisicao` (obrigatória: erro sem número de
  requisição não é gravado, só aparece no log da aplicação). Só metadados: nunca documento, nome nem corpo.
- Chamadas **sem chave válida** (varreduras, chave errada) recebem 401 e **não gravam nada**: não encher a tabela de lixo e não
  gastar o banco com quem não é cliente.

## O que traz

| Arquivo | Conteúdo |
|---|---|
| `banco/postgres/movimentacao.sql`, `banco/oracle/movimentacao.sql` | Tabelas `contador_requisicao`, `movimentacao` e `log_erros`; função/procedure `registrar_requisicao` (numera e grava num único comando) e `registrar_resposta` (completa a linha uma vez só); permissões |
| `app/movimentacao/regras.py` | Máscara de CPF, limite de 32 KB, situações, rotas sem movimentação, `X-Requisicao` |
| `app/movimentacao/gravacao.py` | Fila em memória (1000) e gravação em lotes (100), em segundo plano; falha do banco só vira aviso |
| `app/movimentacao/registros.py` | `RegistroErro`, `RegistroResposta` e o contrato do repositório |
| `app/movimentacao/postgres.py`, `oracle.py` | Repositórios (usam as funções/procedures e a `log_erros`; traduzem falhas do banco) |
| `app/movimentacao/http.py` | `Movimentacao`: numera a chegada (prazo de 1 s), completa a resposta, devolve `X-Requisicao`, registra o erro |
| `tests/unit/test_movimentacao.py`, `tests/integration/test_movimentacao_api.py`, `tests/falhas/` | Regras e fila, a API com repositório falso e o banco fora do ar (porta fechada, roda no CI) |

Sem Oracle, apague `oracle.py` e o teste de Oracle; sem PostgreSQL, o inverso.

## Como incluir

1. **Banco.** Troque `<schema>` (PostgreSQL), `<dono>` (Oracle, schema dono das tabelas, sem login) e `<usuario_app>` (o usuário da
   API) em `banco/<banco>/movimentacao.sql` e execute como administrador. O schema precisa existir e o usuário da API precisa de
   `USAGE` nele (PostgreSQL) ou `CREATE SESSION` (Oracle). A aplicação não recebe acesso às tabelas de movimentação: só executa as
   rotinas; na `log_erros` ela só tem `INSERT`.
2. **Arquivos.** Copie `app/movimentacao/` para `app/movimentacao/` da API e os testes para `tests/` (o `tests/falhas/` traz o hook
   `pytest_asyncio_loop_factories`, necessário no Windows com psycopg async).
3. **`main.py`.** Quatro mudanças:

```python
# (a) imports
from app.movimentacao.http import Movimentacao, registrar_erro
from app.movimentacao.regras import conteudo_da_requisicao, movimentar

# (b) _resposta_erro: registra o erro no log (não faz nada se a movimentação não estiver ligada).
#     Acrescente o parâmetro tipo_excecao e passe-o no handler do 500:
#     tipo_excecao=f"{type(exc).__module__}.{type(exc).__qualname__}"
def _resposta_erro(request, status_code, codigo, mensagem, headers=None, tipo_excecao=None):
    registrar_erro(request, status_code, codigo, mensagem, tipo_excecao)
    ...

# (c) no middleware contexto_da_requisicao, depois de calcular `corpo` e antes do `if grande:`
    movimentacao = Movimentacao.da(request) if chave_ok and movimentar(request.url.path) else None
    if movimentacao:
        await movimentacao.numerar(request, conteudo_da_requisicao(corpo, tamanho, grande))
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
        await app.state.movimentacao.parar()  # grava o que ainda estiver nas filas antes de fechar o pool
```

   O repositório recebe o **pool** que a API já usa (módulo `bancos`, item 5 do LEIAME dele). Passe `lifespan=lifespan` ao `FastAPI(...)`.
4. Rode o Passo 5 (ruff, mypy, pytest). O teste `test_movimentacao_falhas.py` usa os drivers reais contra uma porta sem servidor.

## Regras que valem (mantenha ao adaptar)

- **Prazo de 1 s na chegada**: com o banco fora ou lento, a requisição segue **sem número** (sem `X-Requisicao`, sem movimentação, sem
  `log_erros`) e a API registra o aviso `requisição sem número na movimentação`. A gravação nunca derruba a API.
- **A resposta** é gravada em segundo plano: o cliente não espera por ela. Com o banco fora, a fila enche e as respostas a mais se perdem.
- **Conteúdo**: texto em qualquer formato, até 32 KB; corpo acima do limite da API só grava o tamanho. **Todo CPF é mascarado**
  (`***.982.247-**`) na requisição e na resposta. A chave de API vai num header e nunca é gravada. Se a API recebe outros dados pessoais
  (nome, e-mail, telefone...), eles são gravados como vieram: decida se mascara, **defina o prazo de retenção** (limpeza no cabeçalho do
  SQL) e restrinja o acesso às tabelas.
- **Banco de leitura**: a sessão da API continua somente leitura; só as transações da movimentação pedem escrita
  (`set transaction read write`), com SQL fixo.
- **`DOCUMENTACAO.md`**: documente o header `X-Requisicao`, a tabela (seção de erros) e o aviso de privacidade acima.

## Custo (medido no Motor de Crédito, PostgreSQL, notebook de 2 núcleos)

Gravar a chegada é uma escrita no banco dentro do atendimento. Vazão máxima antes x depois: Python de ~200 para ~140 requisições por
segundo (-29% a -34%); .NET de ~760 para ~530 (-31% a -43%). Se virar problema: conexão própria só para essas escritas e
`synchronous_commit = off` nela (aceita perder as últimas centenas de milissegundos num desligamento brusco).
