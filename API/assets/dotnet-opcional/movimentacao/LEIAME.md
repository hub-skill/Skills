# Módulo opcional: movimentação e log de erros no banco (.NET)

Use quando a API precisa **guardar o que recebeu e o que respondeu** (auditoria, suporte, "o cliente diz que mandou X").
Exige o módulo `bancos` (PostgreSQL e/ou Oracle). Extraído do Motor-de-Credito-Net e validado, com o gêmeo Python, nos dois bancos reais.

## O que faz

- Toda requisição **com chave de API válida** (menos `/health/*` e `/metrics`) ganha um número (`ano` + `requisicao`; o contador
  **volta a 1 todo ano**) e é gravada na tabela `movimentacao`: o que chegou, em qualquer formato, e depois, na mesma linha, o que a
  API respondeu. O cliente recebe o número no header `X-Requisicao` (ex.: `2026-000123`) para informar ao suporte.
- Cada linha tem a situação da requisição (`OK` = aceita; `ER` = recusada, 4xx) e a da resposta (`OK` = 2xx; `ER` = 4xx e 5xx).
- Toda resposta de erro (4xx e 5xx) vai também para `log_erros`, com a chave `ano` + `requisicao` (obrigatória: erro sem número de
  requisição não é gravado, só aparece no log da aplicação). Só metadados: nunca documento, nome nem corpo.
- Chamadas **sem chave válida** (varreduras, chave errada) recebem 401 e **não gravam nada**.

## O que traz

| Arquivo | Conteúdo |
|---|---|
| `banco/postgres/movimentacao.sql`, `banco/oracle/movimentacao.sql` | Tabelas `contador_requisicao`, `movimentacao` e `log_erros`; função/procedure `registrar_requisicao` (numera e grava num único comando) e `registrar_resposta` (completa a linha uma vez só); permissões |
| `src/Movimentacao/RegrasDeMovimentacao.cs` (em `Movimentacao.cs`) | Máscara de CPF, limite de 32 KB, situações, rotas sem movimentação, `X-Requisicao` |
| `src/Movimentacao/GravacaoEmLotes.cs`, `Filas.cs` | Fila em memória (1000) e gravação em lotes (100), em segundo plano (`BackgroundService`); falha do banco só vira aviso |
| `src/Movimentacao/MovimentacaoPostgres.cs`, `MovimentacaoOracle.cs` | Repositórios da movimentação e do log de erros (usam as rotinas do banco) |
| `src/Movimentacao/MovimentacaoMiddleware.cs` | Numera a chegada (prazo de 1 s), guarda a resposta, grava e devolve `X-Requisicao` |
| `src/Movimentacao/RegistroDeErrosHttp.cs` | Liga o `RespostaDeErro` do template ao log de erros |
| `tests/MovimentacaoRegrasTests.cs`, `MovimentacaoFalhasTests.cs` | Regras sem banco; banco fora do ar (porta fechada, roda no CI) |
| `tests/MovimentacaoApiTests.cs` | A API com repositórios falsos (fábrica própria) |

Sem Oracle, apague `MovimentacaoOracle.cs` e o teste de Oracle; sem PostgreSQL, o inverso.

## Como incluir

1. **Banco.** Troque `<schema>` (PostgreSQL), `<dono>` (Oracle, schema dono das tabelas, sem login) e `<usuario_app>` (o usuário da
   API) em `banco/<banco>/movimentacao.sql` e execute como administrador. O schema precisa existir e o usuário da API precisa de
   `USAGE` nele (PostgreSQL) ou `CREATE SESSION` (Oracle). A aplicação não recebe acesso às tabelas de movimentação: só executa as
   rotinas; na `log_erros` ela só tem `INSERT`.
2. **Arquivos.** Copie `src/Movimentacao/` para `src/ProjetoApi/Movimentacao/`, `MovimentacaoRegrasTests.cs` e
   `MovimentacaoFalhasTests.cs` para `tests/ProjetoApi.UnitTests/` e `MovimentacaoApiTests.cs` para `tests/ProjetoApi.IntegrationTests/`.
   Ajuste o namespace `ProjetoApi` junto com o resto do projeto.
3. **`Errors/RespostaDeErro.cs`**: registre o erro no log (acrescente o parâmetro `tipoExcecao` e a chamada no início de `EscreverAsync`):

```csharp
using ProjetoApi.Movimentacao;
...
public static Task EscreverAsync(
    HttpContext contexto, int status, string codigo, string titulo, CancellationToken cancellationToken,
    string? tipoExcecao = null)
{
    ArgumentNullException.ThrowIfNull(contexto);
    RegistroDeErrosHttp.Registrar(contexto, status, codigo, titulo, tipoExcecao);
    ...
```

4. **`Errors/ErroDeDominioHandler.cs`**: passe o tipo da exceção só no 500:

```csharp
var tipoExcecao = status == StatusCodes.Status500InternalServerError ? exception.GetType().FullName : null;
await RespostaDeErro.EscreverAsync(httpContext, status, codigo, mensagem, cancellationToken, tipoExcecao);
```

5. **`Program.cs`**: registre os repositórios (com o `NpgsqlDataSource` que a API já usa, módulo `bancos`), as filas e o middleware, **o primeiro**:

```csharp
using ProjetoApi.Movimentacao;
...
builder.Services.AddSingleton<IRepositorioDeMovimentacao>(new MovimentacaoPostgres(fonteDoBanco, "app"));
builder.Services.AddSingleton<IRepositorioDeErros>(new LogDeErrosPostgres(fonteDoBanco, "app"));
// Oracle: new MovimentacaoOracle(ConexoesOracle.Montar(dataSource, usuario, senha)) e new LogDeErrosOracle(...)
builder.Services.AddSingleton(new MovimentacaoOpcoes(TimeSpan.FromSeconds(1), TamanhoMaximoDoCorpo));
builder.Services.AddSingleton<FilaDeErros>();
builder.Services.AddHostedService<GravadorDeErros>();
builder.Services.AddSingleton<FilaDeRespostas>();
builder.Services.AddHostedService<GravadorDeRespostas>();
...
app.UseMiddleware<MovimentacaoMiddleware>(); // antes de app.UseExceptionHandler()
```

6. **Testes que já existem** (`ApiFactory` e `ApiFactoryConfiguravel`): troque os repositórios por falsos, para não tocarem o banco.
   Troque **todos** os repositórios reais da API (também o de consultas/listas do módulo `bancos`), não só os da movimentação, e use a mesma
   opção `TamanhoMaximoDoCorpo` do `Program.cs` em `MovimentacaoOpcoes`. Se a API carrega `.env`, desligue-o nos testes.
   `MovimentacaoFalsa` e `DestinoDeErrosFalso` estão em `MovimentacaoApiTests.cs`:

```csharp
builder.ConfigureTestServices(servicos =>
{
    servicos.RemoveAll<IRepositorioDeMovimentacao>();
    servicos.AddSingleton<IRepositorioDeMovimentacao>(new MovimentacaoFalsa());
    servicos.RemoveAll<IRepositorioDeErros>();
    servicos.AddSingleton<IRepositorioDeErros>(new DestinoDeErrosFalso());
});
```

7. Rode `dotnet format` (ele também ordena os `using` que você acrescentou) e o Passo 5 (build com 0 avisos, testes).

## Regras que valem (mantenha ao adaptar)

- **Prazo de 1 s na chegada**: com o banco fora ou lento, a requisição segue **sem número** (sem `X-Requisicao`, sem movimentação, sem
  `log_erros`) e a API registra o aviso `requisição sem número na movimentação`. A gravação nunca derruba a API.
- **A resposta** é gravada em segundo plano: o cliente não espera por ela. Com o banco fora, a fila enche e as respostas a mais se perdem.
- **O middleware é o primeiro** e guarda a resposta em memória (JSONs pequenos): é o que permite gravar o que o cliente recebeu,
  inclusive 401, 404, 429 e 500. O `SecurityHeadersMiddleware` usa `OnStarting` por isso (o tratador de exceções limpa os headers).
- **Conteúdo**: texto em qualquer formato, até 32 KB; corpo acima do limite da API só grava o tamanho. **Todo CPF é mascarado**
  (`***.982.247-**`) na requisição e na resposta. A chave de API vai num header e nunca é gravada. Se a API recebe outros dados pessoais
  (nome, e-mail, telefone...), eles são gravados como vieram: decida se mascara, **defina o prazo de retenção** (limpeza no cabeçalho do
  SQL) e restrinja o acesso às tabelas.
- **Banco de leitura**: a sessão da API continua somente leitura; só as transações da movimentação pedem escrita
  (`set transaction read write`), com SQL fixo.
- **`DOCUMENTACAO.md`**: documente o header `X-Requisicao`, a tabela (seção de erros) e o aviso de privacidade acima.

## Custo (medido no Motor de Crédito, PostgreSQL, notebook de 2 núcleos)

Gravar a chegada é uma escrita no banco dentro do atendimento. Vazão máxima antes x depois: .NET de ~760 para ~530 requisições por
segundo (-31% a -43%); Python de ~200 para ~140 (-29% a -34%). Se virar problema: conexão própria só para essas escritas e
`synchronous_commit = off` nela (aceita perder as últimas centenas de milissegundos num desligamento brusco).
