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

Leia também as lições da linguagem (`references/licoes-python.md` ou `references/licoes-dotnet.md`) e, antes de escrever testes, `references/licoes-gerais.md`. Não misture as duas stacks na mesma resposta. Use `references/comparativo.md` apenas se o usuário pedir equivalências ou migração.

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

Se a API usa banco (módulo `bancos`), pergunte também se ela deve **guardar o que recebeu e o que respondeu** (auditoria,
suporte): é o módulo `movimentacao`. Sem resposta, **não** inclua (grava dados da entrada, então é escolha do usuário).

Se o usuário não souber, assuma o padrão seguro e declare a suposição na resposta.

## Passo 4: gerar a partir dos templates

Os templates ficam em `assets/<linguagem>/` com a mesma estrutura de pastas do projeto final. Copie-os para o destino e adapte:

- Python: `assets/python/` contém `app/` (com `__init__.py` em todos os pacotes), `tests/conftest.py` (variáveis de ambiente definidas antes de importar a app), `tests/unit/`, `tests/integration/`, `pyproject.toml` (uv, ruff, mypy strict, pytest), `.env.example` e `postman/`.
- C#: `assets/dotnet/` contém `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `src/ProjetoApi/`, `tests/ProjetoApi.UnitTests/`, `tests/ProjetoApi.IntegrationTests/` e `postman/`.

Ao adaptar, troque o nome do projeto e a regra de negócio em `transformacao.py` ou `TransformacaoService.cs`. Mantenha as demais regras de segurança, a não ser que o usuário justifique a mudança.

### Módulos opcionais (.NET)

Inclua só quando a API precisar; cada pasta tem um `LEIAME.md` com o passo a passo e os testes do módulo:

- `assets/dotnet-opcional/documentos/`: **a API recebe CPF ou CNPJ**. Validação dos dígitos verificadores (inclusive o CNPJ alfanumérico da Receita), máscara do CPF e testes (inclusive de propriedade).
- `assets/dotnet-opcional/entrada-json/`: **a API recebe JSON estruturado** (opção JSON do Passo 3, e o padrão). Exemplo de pedido com cliente e itens, `JsonSerializerOptions` estrito (campo desconhecido, conversão de tipo, obrigatório e `null` = 422), validador com limites em todo campo e 20 testes espelhados nos do Python (a profundidade já é verificada no template base).
- `assets/dotnet-opcional/movimentacao/`: **a API deve guardar o que recebeu e respondeu** (exige `bancos`). **Versão 2 (ULID):** o identificador da requisição é um ULID gerado na API (sem banco, sem contador), devolvido em `X-Requisicao`; uma só gravação por requisição, em segundo plano, com o erro na mesma transação (`movimentacao` e `log_erros`, chave `requisicao_id`); CPF mascarado; só para requisições com chave de API válida; a aplicação só tem `INSERT`. Scripts SQL para PostgreSQL e Oracle (com a migração da versão 1), middleware, repositórios e testes.
- `assets/dotnet-opcional/bancos/`: **a API consulta PostgreSQL e/ou Oracle**. Strings de conexão seguras, tradução das falhas do driver em 503/504 (nunca 500, inclusive o `ORA-50201` do ODP.NET), health check do banco, o corpo do `/health/ready` com banco (`RespostaDeSaudeComBanco`) e testes que não precisam de banco no ar. As versões dos drivers já estão no `Directory.Packages.props`, com a correção da dependência vulnerável do driver Oracle.

Ao incluir um módulo, ajuste o namespace `ProjetoApi` junto com o resto do projeto e rode as verificações do Passo 5 com os testes do módulo.

### Módulos opcionais (Python)

Mesma ideia, em pastas separadas das do .NET (não misture). Cada uma tem um `LEIAME.md`:

- `assets/python-opcional/documentos/`: **a API recebe CPF ou CNPJ**. `normalizar_documento`, dígitos verificadores (inclusive o CNPJ alfanumérico), `mascarar`, `miolo_cpf` e testes com `hypothesis`.
- `assets/python-opcional/entrada-json/`: **a API recebe JSON estruturado** (opção JSON do Passo 3, e o padrão). Exemplo de pedido com cliente e itens, `ModeloDeEntrada` (`extra="forbid"`, `strict=True`), limites em todo campo, dinheiro em centavos e 20 testes espelhados nos do .NET.
- `assets/python-opcional/movimentacao/`: **a API deve guardar o que recebeu e respondeu** (exige `bancos`). Mesma regra do módulo .NET (versão 2, ULID): `requisicao_id`, uma gravação por requisição com o erro junto, `X-Requisicao`, CPF mascarado, só com chave válida. Scripts SQL (PostgreSQL e Oracle), `app/movimentacao/` e testes.
- `assets/python-opcional/bancos/`: **a API consulta PostgreSQL (psycopg 3) e/ou Oracle (python-oracledb)**. `montar_conninfo` sem senha, `options_sessao` (read only + `statement_timeout`), `traduzir_erros_postgres()` / `traduzir_erros_oracle()` que transformam falhas do driver em 503 `banco_indisponivel` ou 504 `banco_timeout` (nunca 500), `resposta_pronto()` (corpo do `/health/ready` com banco) e testes com os drivers reais contra uma porta sem servidor (rodam no CI, sem banco).

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

Validados em 11 e 12/11/2026, no Windows, com PostgreSQL e Oracle reais: **Python** (uv, Python 3.12+) com 55 testes no template, 75 com `entrada-json` e
170 com todos os módulos (ruff, format, mypy strict e bandit limpos; cobertura 100%, 100% e 94%); **.NET** (SDK 10) com 48, 68 e 154 testes
(`dotnet format` sem mudanças, build com 0 avisos, nenhum pacote vulnerável). Coleções Postman validadas com os templates rodando. O detalhe
(datas, correções que cada validação exigiu) está em `references/historico-de-validacao.md`.

### Lições (leia antes de gerar ou revisar)

Cada erro que custou tempo virou uma lição, separada por assunto para não pesar este arquivo:

- `references/licoes-python.md` ou `references/licoes-dotnet.md`: a da linguagem escolhida (build, JSON, timeout, middleware, testes da linguagem).
- `references/licoes-gerais.md`: testes com dados reais, comparação com API de referência, carga de arquivos públicos, ferramentas de console e processo.
- **Banco:** skills `conexao-postgresql` e `conexao-oracle` (Python em `references/fastapi.md`, C# em `references/dotnet.md`, dentro de cada uma); tipos e instantes em `oracle-naming-sinonimos` e `postgresql-naming-schemas`.
- **Container e Kubernetes** (Docker Desktop desligado, caminhos do Git Bash, `.env.docker`, cluster desligado): skills `docker` e `kubernetes`.

## Passo 6: entregar

Salve os arquivos no diretório indicado pelo usuário (ou, se ele não indicar, no diretório atual do projeto) e apresente-os, incluindo a `DOCUMENTACAO.md` na raiz da pasta da API e a coleção em `postman/`. Informe o nível de proteção adotado, as suposições feitas e o que ficou pendente.

Para empacotar em container, use a skill **docker** (Dockerfile Python e .NET separados, as duas na porta 8080).
Para publicar no Kubernetes depois, use a skill **kubernetes** (manifestos comuns às duas linguagens).
