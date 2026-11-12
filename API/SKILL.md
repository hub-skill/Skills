---
name: criar-api
description: Use esta skill sempre que o usuário pedir para gerar, criar, estruturar ou revisar uma API com entrada em texto (string) ou em JSON estruturado, em Python ou em C# (.NET), mesmo que não cite FastAPI, ASP.NET Core ou a linguagem explicitamente, como em "gere uma api em python", "gere uma api em .net", "crie um endpoint que recebe string e devolve string", "uma api que recebe um json com os dados do pedido" ou "preciso de um serviço REST com autenticação por chave". Cobre segurança, validação, timeout, erros, testes, observabilidade, deploy e CI. Não use para APIs em Node, Java, Go ou React, nem para perguntas gerais sobre HTTP sem construção de API.
---

# Criar API (Python ou C#)

Gera APIs seguras e testáveis. As regras gerais vêm do mapa de princípios; a linguagem define a stack, a estrutura e os templates.

## Passo 1: identificar a linguagem

- "python", "fastapi" ou "python 3" → caminho Python.
- ".net", "c#", "csharp", "asp.net" ou "dotnet" → caminho C#.
- Se o pedido não disser a linguagem, pergunte antes de gerar. Não escolha sozinho.

## Passo 2: ler as referências necessárias

Leia sempre `references/principios.md`. Depois leia apenas a pasta da linguagem escolhida:

- Python: `references/python/stack-e-estrutura.md`
- C#: `references/dotnet/stack-e-estrutura.md`

Não misture as duas stacks na mesma resposta. Use `references/comparativo.md` apenas se o usuário pedir equivalências ou migração.

## Passo 3: confirmar o que for ambíguo

**Primeiro, sempre pergunte o formato da entrada**, com as duas opções:

- **String**: a API recebe um único texto (`{"texto": "..."}`). É o template base, sem módulo.
- **JSON**: a API recebe um JSON estruturado (vários campos, objetos aninhados, listas). Template base + módulo
  `entrada-json` da linguagem. Pergunte também os campos (nome, tipo, obrigatório ou não, limites), ou deduza-os do
  pedido e mostre-os antes de gerar.

**Se o usuário não escolher, use JSON** e diga na resposta que essa foi a suposição. Não pergunte de novo.

Depois, confirme no máximo três pontos (veja `references/pontos-a-confirmar.md`):

- chave única ou múltiplos clientes;
- uma instância ou várias;
- entrada usada em shell, SQL, caminho, template ou `eval`;
- API pública ou interna.

Se o usuário não souber, assuma o padrão seguro e declare a suposição na resposta.

## Passo 4: gerar a partir dos templates

Os templates ficam em `assets/<linguagem>/` com a mesma estrutura de pastas do projeto final. Copie-os para o destino e adapte:

- Python: `assets/python/` contém `app/` (com `__init__.py` em todos os pacotes), `tests/conftest.py` (variáveis de ambiente definidas antes de importar a app), `tests/unit/`, `tests/integration/`, `pyproject.toml` (uv, ruff, mypy strict, pytest), `.env.example` e `postman/`.
- C#: `assets/dotnet/` contém `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `src/ProjetoApi/`, `tests/ProjetoApi.UnitTests/`, `tests/ProjetoApi.IntegrationTests/` e `postman/`.

Ao adaptar, troque o nome do projeto e a regra de negócio em `transformacao.py` ou `TransformacaoService.cs`. Mantenha as demais regras de segurança, a não ser que o usuário justifique a mudança.

### Módulos opcionais (.NET)

Inclua só quando a API precisar; cada pasta tem um `LEIAME.md` com o passo a passo e os testes do módulo:

- `assets/dotnet-opcional/documentos/`: **a API recebe CPF ou CNPJ**. Validação dos dígitos verificadores (inclusive o CNPJ alfanumérico da Receita), máscara do CPF e testes (inclusive de propriedade).
- `assets/dotnet-opcional/entrada-json/`: **a API recebe JSON estruturado** (opção JSON do Passo 3, e o padrão). Exemplo de pedido com cliente e itens, `JsonSerializerOptions` estrito (campo desconhecido, conversão de tipo, obrigatório e `null` = 422), validador com limites em todo campo, middleware de profundidade (JSON fundo demais = 400, como no Python) e 20 testes espelhados nos do Python.
- `assets/dotnet-opcional/bancos/`: **a API consulta PostgreSQL e/ou Oracle**. Strings de conexão seguras, tradução das falhas do driver em 503/504 (nunca 500, inclusive o `ORA-50201` do ODP.NET), health check do banco e testes que não precisam de banco no ar. As versões dos drivers já estão no `Directory.Packages.props`, com a correção da dependência vulnerável do driver Oracle.

Ao incluir um módulo, ajuste o namespace `ProjetoApi` junto com o resto do projeto e rode as verificações do Passo 5 com os testes do módulo.

### Módulos opcionais (Python)

Mesma ideia, em pastas separadas das do .NET (não misture). Cada uma tem um `LEIAME.md`:

- `assets/python-opcional/documentos/`: **a API recebe CPF ou CNPJ**. `normalizar_documento`, dígitos verificadores (inclusive o CNPJ alfanumérico), `mascarar`, `miolo_cpf` e testes com `hypothesis`.
- `assets/python-opcional/entrada-json/`: **a API recebe JSON estruturado** (opção JSON do Passo 3, e o padrão). Exemplo de pedido com cliente e itens, `ModeloDeEntrada` (`extra="forbid"`, `strict=True`), limites em todo campo, dinheiro em centavos e 20 testes espelhados nos do .NET.
- `assets/python-opcional/bancos/`: **a API consulta PostgreSQL (psycopg 3) e/ou Oracle (python-oracledb)**. `montar_conninfo` sem senha, `options_sessao` (read only + `statement_timeout`), `traduzir_erros_postgres()` / `traduzir_erros_oracle()` que transformam falhas do driver em 503 `banco_indisponivel` ou 504 `banco_timeout` (nunca 500), e testes com os drivers reais contra uma porta sem servidor (rodam no CI, sem banco).

Ao incluir um módulo, rode as verificações do Passo 5 com os testes dele.

Gere também um README com como rodar, testar e configurar as variáveis de ambiente. Em C#, a chave de API é `Api__ApiKey`; em Python, `API_KEY`.

### Coleção Postman (obrigatória)

Toda API gerada sai com uma coleção Postman em **`postman/<Projeto>.postman_collection.json`**, a partir do modelo da
linguagem (não misture: `assets/python/postman/` ou `assets/dotnet/postman/`). Ela já cobre o contrato comum: rota de
negócio, 401 (sem chave, chave errada, rota inexistente sem chave), 404, 405, 400, 422, 413, 429 e os health checks,
com testes que conferem status, `application/problem+json`, os quatro campos e o `code`.

- Adapte o que está marcado com **ADAPTE** nas descrições: nome da coleção, rota, corpos válidos e inválidos, testes
  do sucesso, tamanho do corpo do 413 (acima do limite da API) e o corpo do `/health/ready` se houver banco.
- Mantenha a autenticação herdada da coleção (`X-API-Key: {{api_key}}`) e `api_key` **vazia** e do tipo *secret*:
  nunca grave uma chave real no arquivo.
- Ajuste `base_url` à porta usada no começo rápido da `DOCUMENTACAO.md`.
- Valide de verdade: com a API rodando, execute cada pedido (Postman/newman, ou reexecutando método, URL, corpo e
  autenticação de cada item) e confira o status esperado; para o 429, suba a API com limite baixo.

### Documentação de uso (obrigatória)

Toda API gerada sai com uma **`DOCUMENTACAO.md` na raiz da pasta da API**, para quem consome e para quem opera: para que serve e utilidades (casos de uso), como funciona, começo rápido, referência completa dos endpoints (campos de entrada e saída), regras de negócio, erros, exemplos de integração, operação, configuração, segurança/LGPD, limitações e solução de problemas.

- C#: parta do modelo `assets/dotnet/DOCUMENTACAO.md` e substitua todos os `<marcadores>`.
- Python: parta do modelo `assets/python/DOCUMENTACAO.md` e substitua todos os `<marcadores>`.
- Escreva-a **depois** do Passo 5, com requisições e respostas **reais** obtidas rodando a API; não invente exemplos nem números (tempos, limites).
- O README continua curto (rodar e testar); os detalhes ficam na documentação.

## Passo 5: verificar

Execute as verificações da linguagem quando o ambiente permitir:

- Python: `ruff check`, `ruff format --check`, `mypy app`, `pytest`.
- C#: `dotnet format --verify-no-changes`, `dotnet build`, `dotnet test`, `dotnet list package --vulnerable --include-transitive`.

Se não conseguir executar, diga isso ao usuário. Não afirme que os testes passaram sem tê-los rodado.

### Situação dos templates

- **C# (.NET): validado** em 11/11/2026, no Windows, com SDK 10.0.404, em dois cenários: template sozinho (29 testes: 10 unitários e 19 de integração) e template com os dois módulos opcionais aplicados conforme os LEIAME (66 testes: 47 unitários e 19 de integração). Nos dois: `dotnet format` sem mudanças, build com 0 avisos e 0 erros (avisos tratados como erro) e nenhum pacote vulnerável. Também foi usado de base para uma API real (Motor-de-Credito-Net), com PostgreSQL (Npgsql) e Oracle (ODP.NET).
  - `ComportamentoDaApiTests.cs` traz os testes genéricos que toda API gerada deve manter: 401 com formato padrão e `WWW-Authenticate`, rota inexistente (401 sem chave, 404 com chave), 413, JSON inválido = 400, health e cabeçalhos de segurança, 429 com `Retry-After`, 504, 500 genérico sem vazar detalhes e documentação só quando ligada. Ao adaptar, troque só `Url` e `CorpoValido`.
  - Revalidado em 11/11/2026 depois da correção do timeout (abaixo), nos dois cenários: template sozinho com 30 testes (11 unitários e 19 de integração) e template com os dois módulos opcionais com 67 testes (48 unitários e 19 de integração). Nos dois: format sem mudanças, build com 0 avisos e 0 erros e nenhum pacote vulnerável.
  - Correção do timeout: `TransformacaoService` usava só `Task.Run(..., ct)`, cujo token apenas evita **iniciar** o trabalho; com a regra lenta, o 504 só saía quando ela terminava. Agora usa `.WaitAsync(ct)`, e o teste `Servico_para_de_esperar_quando_o_tempo_acaba_durante_o_trabalho` exercita o serviço real (o teste de 504 da integração usa um serviço falso e não pegava o problema). É o mesmo erro do `abandon_on_cancel` do Python.
  - Correção incluída: com `EnableDocs=true`, `MapOpenApi`/`MapScalarApiReference` precisam de `AllowAnonymous()`; sem isso a política padrão de autenticação devolvia 401 na documentação.
- **Contrato comum de erro e health (12/11/2026)**: os dois templates respondem igual, vindo da unificação feita nos Motores de Crédito (14 cenários conferidos lado a lado com as duas APIs rodando). Erro em RFC 9457 com `application/problem+json` e exatamente `title`, `status`, `code` e `request_id`; 429 = `limite_excedido`; títulos em português para 401/404/405; sem chave, rota inexistente e método errado = 401 com `WWW-Authenticate: APIKey`; health em JSON `{"status":"ok"}`. Os testes conferem o formato com `tests/contrato.py` (Python) e `ContratoDeErro.cs` (.NET), que verificam as mesmas coisas. Revalidado nos dois cenários de cada linguagem: Python com 36 testes (template) e 76 (com os módulos), 100% de cobertura, ruff e mypy limpos; .NET com 31 testes (11 unitários e 20 de integração) e 68 (48 e 20), format sem mudanças, build com 0 avisos.
- **Entrada String ou JSON (12/11/2026)**: módulo `entrada-json` nas duas linguagens, com o mesmo contrato: corpo ilegível (sintaxe ou profundidade) = 400 `requisicao_invalida`; JSON válido com forma errada (campo desconhecido, tipo errado, obrigatório ausente, `null`, limite, lista no lugar de objeto) = 422 `entrada_invalida`. Os templates base também passaram a seguir essa regra (antes o .NET respondia 400 para tipo errado e o Python 400 `http_400` em inglês para JSON fundo demais). Validado: Python base 41 testes e com o módulo 61 (100% de cobertura, ruff, mypy e bandit limpos); .NET base 36 (11 + 25) e com o módulo 56 (11 + 45), format sem mudanças, 0 avisos.
- **Coleção Postman (12/11/2026)**: `assets/python/postman/` e `assets/dotnet/postman/`, cada uma com 16 pedidos. Validadas com os dois templates rodando: todos os pedidos com o status esperado, erros em `application/problem+json` com os quatro campos, `resultado` correto e health `{"status":"ok"}`; o 429 (`limite_excedido`, `Retry-After: 60`) conferido com o limite em 2/min. Mesmo modelo usado nos Motores de Crédito.
- **Python: validado** em 11/11/2026, no Windows, com uv e Python 3.12+, em dois cenários: template sozinho (33 testes, 100% de cobertura) e template com os dois módulos opcionais (73 testes, 100% de cobertura). Nos dois: `ruff check` e `ruff format --check` limpos, `mypy app` strict sem erros; no template sozinho, `bandit` e `pip-audit` também limpos. A versão anterior (10/11/2026, com erros de ruff/mypy e 10 testes falhando) foi reescrita.
  - `tests/integration/test_comportamento_api.py` traz os testes genéricos que toda API gerada deve manter (equivalente ao `ComportamentoDaApiTests.cs`): 401 com formato padrão e `WWW-Authenticate`, 404, 413, JSON inválido = 400, health e cabeçalhos de segurança, `/metrics`, 429 com `Retry-After`, 504, 500 genérico sem vazar detalhes e documentação só quando ligada. Ao adaptar, troque só a URL e o corpo válido.

### Lições do Python

- `anyio.to_thread.run_sync` **ignora o cancelamento** por padrão: com `fail_after`, o timeout só dispara depois que a thread termina (o teste de 504 recebia 200). Use `abandon_on_cancel=True` (anyio >= 4.1).
- Defina as variáveis de ambiente no `tests/conftest.py` **antes** de importar `app.main`; a configuração é lida na importação.
- Para testar o 500 genérico com `httpx.ASGITransport`, use `raise_app_exceptions=False`; para 504/500, injete a regra de negócio por `Depends` e troque com `app.dependency_overrides`.
- psycopg async no Windows não funciona com o ProactorEventLoop: `uvicorn --loop asyncio:SelectorEventLoop` e, nos testes, o hook `pytest_asyncio_loop_factories`. No Windows, porta fechada chega como `ConnectionTimeout` (não "connection refused").
- Com banco, os tempos vão em escada: consulta (`statement_timeout`/`call_timeout`) e espera do pool **menores** que `REQUEST_TIMEOUT_SECONDS`. Com o banco fora, o pool espera o prazo dele antes de desistir; se a requisição tiver limite menor, o cliente recebe 504 em vez de 503 (visto no Motor de Crédito com pool de 5 s e requisição de 2 s).
- Nunca registre `exc.pgconn` em log (contém a senha). As mensagens do PostgreSQL em português chegam com acentuação quebrada: decida pelo `sqlstate`, não pelo texto.
- O FastAPI responde `400 http_400` "There was an error parsing the body" (em inglês) quando o parser de JSON estoura a recursão (JSON aninhado fundo demais). O template trata o 400 do framework como `requisicao_invalida`.
- Pydantic aceita campo desconhecido em silêncio e converte `"2"` em `2` por padrão. Com entrada JSON, use `extra="forbid"` e `strict=True` (base `ModeloDeEntrada` do módulo).
- `ruff format .` formata também scripts fora da API (ex.: um `gerar_pdf.py` na raiz). Formate só `app tests`.
- Escreva `pyproject.toml` com a ferramenta de edição de arquivos, não com `Set-Content` do PowerShell 5.1 (grava BOM/ANSI e quebra o parser TOML).

### Lições do .NET

- Drivers de banco podem trazer dependências transitivas vulneráveis (ex.: `Oracle.ManagedDataAccess.Core` traz `System.Security.Cryptography.Pkcs` 8.0.1). Com `TreatWarningsAsErrors`, o build falha com NU1902/NU1903. Corrija fixando a versão no `Directory.Packages.props` com `<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>` e um `PackageVersion` corrigido; nunca desligue o aviso.
- Npgsql 10 em container Linux (imagem chiseled): o padrão `GssEncryptionMode=Prefer` tenta carregar a `libgssapi_krb5.so.2`, que não existe, e registra `Cannot load library libgssapi_krb5.so.2`. Sem Kerberos, use `GssEncryptionMode.Disable` (já no módulo `bancos`; revalidado com os 67 testes em 11/11/2026).
- O `GlobalLimiter` do rate limit vale para **todos** os endpoints, inclusive `/health/*` e `/metrics`. No Kubernetes as probes saem do IP do nó; um 429 na liveness reinicia o pod saudável (visto no teste de troca: 169 respostas 429). O template usa `.DisableRateLimiting()` nesses três, e o teste de 429 confere que eles seguem 200 com o limite estourado (revalidado: 30 e 67 testes, 11/11/2026).
- O `WebApplicationFactory` sobe em **Development** por padrão, e alguns comportamentos mudam com o ambiente. Exemplo real: `RouteHandlerOptions.ThrowOnBadRequest` só é `true` em Development; em produção o JSON malformado saía `400` "Bad Request" fora do formato, e os testes passavam. O template fixa `ThrowOnBadRequest = true` e as fábricas de teste usam `UseEnvironment("Production")`; sem a correção, o teste de JSON inválido falha.
- `AddProblemDetails` + `CustomizeProblemDetails` não permite tirar `type` e `traceId` do corpo. Para um formato exato, escreva o erro com um escritor próprio (`RespostaDeErro`) no `IExceptionHandler`, no `UseStatusCodePages` e no `OnRejected`. Mantenha `AddProblemDetails()` registrado: sem ele o `UseExceptionHandler()` lança `InvalidOperationException` na inicialização.
- Record com `string` não anulável **não** impede `null`: `{}` ou `{"texto": null}` chegava ao endpoint com `Texto = null` e o `Trim()` dava **500** (achado ao testar a entrada JSON). O template liga `RespectNullableAnnotations` e `RespectRequiredConstructorParameters`, e o teste `Json_valido_com_forma_errada_responde_422` cobre `{}` e `null`.
- O `System.Text.Json` lança `JsonException` para tudo; só a `InnerException` diz se é sintaxe (`JsonReaderException`, interna: comparada pelo nome) ou forma (tipo, campo, obrigatório). O `ErroDeDominioHandler` usa isso para 400 x 422; os testes de JSON malformado (400) e tipo errado (422) pegam se o .NET mudar.
- O padrão Web do ASP.NET aceita número em texto (`"2"`) por `NumberHandling.AllowReadingFromString`. Com entrada JSON, use `JsonNumberHandling.Strict`.
- Atualizações do .NET SDK pelo Windows Update deixam o SDK inconsistente durante a instalação (erro MSB4014/MSB4236 de *workload manifest*). Não é erro do projeto: espere o instalador terminar e rode de novo.

## Passo 6: entregar

Salve os arquivos no diretório indicado pelo usuário (ou, se ele não indicar, no diretório atual do projeto) e apresente-os, incluindo a `DOCUMENTACAO.md` na raiz da pasta da API e a coleção em `postman/`. Informe o nível de proteção adotado, as suposições feitas e o que ficou pendente.

Para empacotar em container, use a skill **docker** (Dockerfile Python e .NET separados, as duas na porta 8080).
Para publicar no Kubernetes depois, use a skill **kubernetes** (manifestos comuns às duas linguagens).
