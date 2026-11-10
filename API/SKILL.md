---
name: criar-api
description: Use esta skill sempre que o usuário pedir para gerar, criar, estruturar ou revisar uma API com entrada e saída em texto, em Python ou em C# (.NET), mesmo que não cite FastAPI, ASP.NET Core ou a linguagem explicitamente, como em "gere uma api em python", "gere uma api em .net", "crie um endpoint que recebe string e devolve string" ou "preciso de um serviço REST com autenticação por chave". Cobre segurança, validação, timeout, erros, testes, observabilidade, deploy e CI. Não use para APIs em Node, Java, Go ou React, nem para perguntas gerais sobre HTTP sem construção de API.
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

Antes de gerar, confirme no máximo três pontos (veja `references/pontos-a-confirmar.md`):

- chave única ou múltiplos clientes;
- uma instância ou várias;
- entrada usada em shell, SQL, caminho, template ou `eval`;
- API pública ou interna.

Se o usuário não souber, assuma o padrão seguro e declare a suposição na resposta.

## Passo 4: gerar a partir dos templates

Os templates ficam em `assets/<linguagem>/` com a mesma estrutura de pastas do projeto final. Copie-os para o destino e adapte:

- Python: `assets/python/` contém `app/`, `tests/`, `pyproject.toml` e `.env.example`.
- C#: `assets/dotnet/` contém `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `src/ProjetoApi/` e `tests/ProjetoApi.IntegrationTests/`.

Ao adaptar, troque o nome do projeto e a regra de negócio em `transformacao.py` ou `TransformacaoService.cs`. Mantenha as demais regras de segurança, a não ser que o usuário justifique a mudança.

Gere também um README com como rodar, testar e configurar as variáveis de ambiente. Em C#, a chave de API é `Api__ApiKey`; em Python, `API_KEY`.

## Passo 5: verificar

Execute as verificações da linguagem quando o ambiente permitir:

- Python: `ruff check`, `ruff format --check`, `mypy app`, `pytest`.
- C#: `dotnet format --verify-no-changes`, `dotnet build`, `dotnet test`.

Se não conseguir executar, diga isso ao usuário. Não afirme que os testes passaram sem tê-los rodado. Os templates e as versões de pacote ainda precisam ser validados em um ambiente real.

## Passo 6: entregar

Salve os arquivos no diretório indicado pelo usuário (ou, se ele não indicar, no diretório atual do projeto) e apresente-os. Informe o nível de proteção adotado, as suposições feitas e o que ficou pendente.
