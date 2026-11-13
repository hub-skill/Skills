# Módulo opcional: movimentação e log de erros no banco (.NET), versão 2 (ULID)

Use quando a API precisa **guardar o que recebeu e o que respondeu** (auditoria, suporte, "o cliente diz que mandou X"). Exige o módulo
`bancos` (PostgreSQL e/ou Oracle). **Pergunte antes de incluir**: grava dados da entrada, tem custo e exige retenção (veja "Quando usar").

## O que faz

- Toda requisição **com chave de API válida** (menos `/health/*` e `/metrics`) ganha um **ULID** de 26 caracteres, gerado na própria API
  (sem banco), devolvido ao cliente no header `X-Requisicao` (ex.: `01JC4M7Z9Q8T2X5V6B3N0WYKHD`) para informar ao suporte. O ULID carrega a hora
  de chegada, é único na prática e, em ordem alfabética, é a ordem cronológica. **Não é sequencial e não reinicia a cada ano.**
- Depois da resposta, **uma só gravação**, em segundo plano: o que chegou, o que a API respondeu e, se foi erro (4xx/5xx), os metadados do erro
  (tabela `log_erros`), tudo na mesma transação. O banco nunca está no caminho da resposta.
- Situação da requisição: `OK` = aceita; `ER` = recusada (4xx). Situação da resposta: `OK` = 2xx; `ER` = 4xx e 5xx.
- `log_erros`: só metadados (nunca documento, nome nem corpo). Chamadas **sem chave válida** recebem 401 e **não gravam nada**.

## O que traz

| Arquivo | Conteúdo |
|---|---|
| `banco/postgres/movimentacao.sql`, `banco/oracle/movimentacao.sql` | Tabelas `movimentacao` (chave `requisicao_id`) e `log_erros` (com chave estrangeira); instantes em `timestamptz` / `TIMESTAMP WITH TIME ZONE`; a aplicação só tem `INSERT`. Traz a migração da versão 1 (ano + contador) |
| `src/Movimentacao/Ulid.cs` | Gerador (gerador criptográfico), validação e leitura da hora do ULID |
| `src/Movimentacao/Movimentacao.cs` (`RegrasDeMovimentacao`) | Registro da requisição, interface do repositório, máscara de CPF, limite de 32 KB, situações, rotas sem movimentação |
| `src/Movimentacao/GravacaoEmLotes.cs`, `Filas.cs` | Fila em memória (1000) e gravação em lotes (100), em segundo plano (`BackgroundService`); falha do banco só vira aviso |
| `src/Movimentacao/MovimentacaoPostgres.cs`, `MovimentacaoOracle.cs` | Repositórios: um lote por transação (movimentação e erro juntos) |
| `src/Movimentacao/MovimentacaoMiddleware.cs`, `RegistroDeErrosHttp.cs`, `LogDeErros.cs` | Gera o ULID, guarda a resposta, grava e devolve `X-Requisicao`; liga o `RespostaDeErro` do template ao registro do erro |
| `tests/MovimentacaoRegrasTests.cs`, `MovimentacaoFalhasTests.cs` | Regras e ULID sem banco; banco fora do ar (porta fechada, roda no CI) |
| `tests/MovimentacaoApiTests.cs` | A API com repositório falso (fábrica própria) |

Sem Oracle, apague `MovimentacaoOracle.cs` e o teste de Oracle; sem PostgreSQL, o inverso.

## Como incluir

1. **Banco.** Troque `<schema>` (PostgreSQL), `<dono>` (Oracle, schema dono das tabelas, sem login) e `<usuario_app>` (o usuário da API) em
   `banco/<banco>/movimentacao.sql` e execute como administrador. O schema precisa existir e o usuário da API precisa de `USAGE` nele (PostgreSQL)
   ou `CREATE SESSION` (Oracle). Se já existe a **versão 1**, remova os objetos antigos (comandos no cabeçalho do SQL): a versão 2 não converte dados.
2. **Arquivos.** Copie `src/Movimentacao/` para `src/ProjetoApi/Movimentacao/`, `MovimentacaoRegrasTests.cs` e `MovimentacaoFalhasTests.cs` para
   `tests/ProjetoApi.UnitTests/` e `MovimentacaoApiTests.cs` para `tests/ProjetoApi.IntegrationTests/`. Ajuste o namespace `ProjetoApi`.
3. **`Errors/RespostaDeErro.cs`**: registre o erro (acrescente o parâmetro `tipoExcecao` e a chamada no início de `EscreverAsync`):

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

5. **`Program.cs`**: registre o repositório (com o `NpgsqlDataSource` que a API já usa, módulo `bancos`), a fila e o middleware, **o primeiro**:

```csharp
using ProjetoApi.Movimentacao;
...
builder.Services.AddSingleton<IRepositorioDeMovimentacao>(new MovimentacaoPostgres(fonteDoBanco, "app"));
// Oracle: new MovimentacaoOracle(ConexoesOracle.Montar(dataSource, usuario, senha))
builder.Services.AddSingleton(new MovimentacaoOpcoes(TamanhoMaximoDoCorpo));
builder.Services.AddSingleton<FilaDeMovimentacao>();
builder.Services.AddHostedService<GravadorDeMovimentacao>();
...
app.UseMiddleware<MovimentacaoMiddleware>(); // antes de app.UseExceptionHandler()
```

6. **Testes que já existem** (`ApiFactory` e `ApiFactoryConfiguravel`): troque o repositório por falso, para não tocar o banco. Troque **todos** os
   repositórios reais da API (também os de consulta do módulo `bancos`), não só o da movimentação, e use a mesma opção `TamanhoMaximoDoCorpo` do
   `Program.cs` em `MovimentacaoOpcoes`. Se a API carrega `.env`, desligue-o nos testes. `MovimentacaoFalsa` está em `MovimentacaoApiTests.cs`:

```csharp
builder.ConfigureTestServices(servicos =>
{
    servicos.RemoveAll<IRepositorioDeMovimentacao>();
    servicos.AddSingleton<IRepositorioDeMovimentacao>(new MovimentacaoFalsa());
});
```

7. Rode `dotnet format` (ele também ordena os `using` que você acrescentou) e o Passo 5 (build com 0 avisos, testes).

## Regras que valem (mantenha ao adaptar)

- **O número da requisição nasce na API**: nunca depende do banco. Com o banco fora ou lento, a resposta sai normalmente com `X-Requisicao`; a fila
  enche e as gravações a mais se perdem (a API avisa no log). A gravação nunca derruba a API.
- **A resposta e a gravação**: o middleware é **o primeiro** e guarda a resposta em memória (JSONs pequenos); é o que permite gravar o que o cliente
  recebeu, inclusive 401, 404, 429 e 500. O `SecurityHeadersMiddleware` usa `OnStarting` por isso (o tratador de exceções limpa os headers).
- **Conteúdo**: texto em qualquer formato, até 32 KB; corpo acima do limite da API só grava o tamanho. **Todo CPF é mascarado** (`***.982.247-**`) na
  requisição e na resposta. A chave de API vai num header e nunca é gravada. Se a API recebe outros dados pessoais (nome, e-mail, telefone...), eles
  são gravados como vieram: decida se mascara, **defina o prazo de retenção** (limpeza no cabeçalho do SQL) e restrinja o acesso às tabelas.
- **Quem já gravou a requisição não é apagado**: a chegada **não** é gravada antes do processamento. Se o processo cair no meio da requisição, ela só
  existe no log da aplicação.
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
  a chave primária é a garantia final (a gravação em lote que repetir é recusada inteira).
- **Ordenado pela hora**, não **sequencial**: não há "requisição nº 123 do ano" nem como detectar requisição faltando pela numeração. Se isso for
  exigência, este módulo não serve (use um contador no banco, com o custo medido de -29% a -43% de vazão na versão 1) ou um hash encadeado.
- O gerador usa `RandomNumberGenerator` (criptográfico). Não troque por `Random`.
- Hora com 48 bits: vale até o ano 10889.
