<!--
MODELO da documentação de uso. Ao gerar a API:
  1. Substitua todo <marcador> pelo conteúdo real e apague estes comentários.
  2. Use exemplos reais (requisições e respostas obtidas rodando a API), nunca inventados.
  3. Apague as seções que não se aplicam (ex.: "Operação" se a API não tiver carga ou scripts).
  4. Salve como DOCUMENTACAO.md na raiz da pasta da API (ao lado do README.md).
O README é curto (como rodar e testar); esta documentação é para quem consome e para quem opera a API.
-->

# <Nome da API> – Documentação de uso

Versão <versão> · <data>

## Sumário

1. [Para que serve](#1-para-que-serve)
2. [Como funciona](#2-como-funciona)
3. [Começando em 5 minutos](#3-começando-em-5-minutos)
4. [Referência da API](#4-referência-da-api)
5. [Regras de negócio](#5-regras-de-negócio)
6. [Erros](#6-erros)
7. [Exemplos de integração](#7-exemplos-de-integração)
8. [Operação](#8-operação)
9. [Configuração](#9-configuração)
10. [Segurança e LGPD](#10-segurança-e-lgpd)
11. [Limitações](#11-limitações)
12. [Solução de problemas](#12-solução-de-problemas)

---

## 1. Para que serve

<Uma ou duas frases: o que a API recebe e o que devolve, para quem.>

<Se a resposta tiver resultados/estados possíveis, liste-os:>

| Resultado | Significado | O que fazer |
|---|---|---|
| <valor> | <significado> | <ação esperada do cliente> |

### Utilidades

<Casos de uso concretos, um por item: em que processo do negócio a API entra e que problema resolve.>

- **<Uso 1>**: <descrição>.
- **<Uso 2>**: <descrição>.

### Fontes de dados

<Se a API consulta bancos, listas ou serviços externos: o que é, de onde vem, frequência de atualização.>

---

## 2. Como funciona

<Diagrama simples em texto do fluxo (cliente → API → banco/serviço) e 2 a 4 passos explicando o caminho de uma
requisição. Inclua o tempo típico de resposta medido.>

---

## 3. Começando em 5 minutos

Pré-requisitos: [uv](https://docs.astral.sh/uv/), Python 3.12+<, banco X acessível> e o arquivo `.env` preenchido
(ver [Configuração](#9-configuração)).

```powershell
cd <pasta da API>
uv sync
uv run uvicorn app.main:app --port 8000
```

<Se a API usar psycopg async no Windows: acrescente `--loop asyncio:SelectorEventLoop`.>

Primeira chamada (PowerShell), lendo a chave do `.env`:

```powershell
$chave = ((Get-Content .env | Where-Object { $_ -like 'API_KEY=*' }) -replace '^API_KEY=','')
Invoke-RestMethod -Method Post http://127.0.0.1:8000/api/v1/<rota> `
  -Headers @{ 'X-API-Key' = $chave } -ContentType 'application/json' `
  -Body '<corpo de exemplo>'
```

Resultado esperado: <o que deve voltar>.

---

## 4. Referência da API

### Autenticação

As rotas de negócio exigem o header `X-API-Key: <valor de API_KEY>`. A chave é comparada em tempo constante.
Sem chave ou com chave errada: **401** (com `WWW-Authenticate: APIKey`).

### `POST /api/v1/<rota>`

<O que faz.>

**Requisição** (`Content-Type: application/json`, corpo de até <tamanho>):

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `<campo>` | <tipo> | <sim/não> | <limites, formato, validações> |

```json
<exemplo de requisição>
```

**Resposta 200:**

| Campo | Descrição |
|---|---|
| `<campo>` | <descrição> |

```json
<exemplo real de resposta>
```

### Outros endpoints

| Endpoint | Autenticação | Uso |
|---|---|---|
| `GET /health/live` | não | A aplicação está no ar (não consulta dependências) |
| `GET /health/ready` | não | A aplicação e as dependências (ex.: banco) respondem; `503` se alguma falhar |
| `GET /metrics` | não | Métricas Prometheus. **Exponha só na rede interna** |
| `GET /docs`, `/redoc`, `/openapi.json` | não | Documentação interativa e contrato. Só com `ENABLE_DOCS=true` |

### Limites

| Limite | Valor padrão | Configuração |
|---|---|---|
| Requisições por IP | 60 por minuto | `RATE_LIMIT` (ex.: `60/minute`) |
| Tempo total da requisição | <segundos> s | `REQUEST_TIMEOUT_SECONDS` |
| Tamanho do corpo | <tamanho> | fixo (`TAMANHO_MAXIMO_CORPO` em `app/main.py`) |

### Headers de resposta

| Header | Valor |
|---|---|
| `X-Request-ID` | Identificador da requisição. **Informe ao suporte** em caso de erro |
| `X-Content-Type-Options` / `X-Frame-Options` / `Strict-Transport-Security` | Proteções do navegador |
| `Retry-After` | Nas respostas 429: segundos até poder tentar de novo |

---

## 5. Regras de negócio

<As regras que a API aplica, com os códigos/motivos que aparecem na resposta. Se a política for provisória ou
depender de validação de outra área, diga isso explicitamente.>

---

## 6. Erros

Formato único, sempre com `code` e `request_id`:

```json
{ "error": { "code": "entrada_invalida", "message": "Dados de entrada inválidos.", "request_id": "..." } }
```

| HTTP | `code` | Quando | O cliente deve |
|---|---|---|---|
| 400 | `requisicao_invalida` | JSON malformado | Corrigir o corpo |
| 401 | `http_401` | Sem `X-API-Key` ou chave errada | Conferir a chave |
| 404 | `http_404` | Rota inexistente | Conferir a URL |
| 413 | `corpo_muito_grande` | Corpo acima do limite | Reduzir o corpo |
| 422 | `entrada_invalida` | Entrada inválida | Corrigir; **não repetir** |
| 429 | `limite_excedido` | Limite de requisições | Esperar `Retry-After` |
| 500 | `erro_interno` | Erro inesperado | Informar o `request_id` |
| 504 | `timeout` | Processamento lento demais | Tentar de novo depois |
| <outros> | <code> | <quando> | <ação> |

As mensagens de erro nunca repetem a entrada nem detalhes internos. Repita só 429, 503 e 504, com espera
crescente; nunca repita erros de entrada (400, 413, 422).

---

## 7. Exemplos de integração

### curl

```bash
curl -X POST http://127.0.0.1:8000/api/v1/<rota> \
  -H "X-API-Key: $API_KEY" -H "Content-Type: application/json" \
  -d '<corpo de exemplo>'
```

### Python

```python
import os

import httpx

resposta = httpx.post(
    "http://127.0.0.1:8000/api/v1/<rota>",
    headers={"X-API-Key": os.environ["API_KEY"]},
    json={<campos>},
    timeout=15,
)
resposta.raise_for_status()
print(resposta.json())
```

<Como a API se encaixa no fluxo do sistema cliente (o que fazer com cada resultado).>

---

## 8. Operação

<Tarefas de quem opera: cargas de dados, agendamentos, migrações, scripts, monitoramento.
Apague a seção se a API não tiver nada disso.>

Produção: `gunicorn app.main:app -k uvicorn_worker.UvicornWorker -w <n> --timeout <s> --graceful-timeout <s>`,
com timeouts maiores que `REQUEST_TIMEOUT_SECONDS` e menores que os do proxy.

---

## 9. Configuração

Arquivo `.env` na raiz da API (**nunca versionado**; modelo em `.env.example`). Variáveis de ambiente já
definidas têm prioridade.

| Variável | Padrão | Descrição |
|---|---|---|
| `API_KEY` | – (obrigatória) | Chave dos clientes, mínimo 32 caracteres. A API não sobe sem ela |
| `APP_ENV` | `dev` | `dev`, `staging` ou `prod` |
| `ALLOWED_HOSTS` | `["localhost","127.0.0.1"]` | Hosts aceitos (JSON) |
| `ALLOWED_ORIGINS` | `[]` | Origens liberadas para CORS (JSON) |
| `ENABLE_DOCS` | `false` | Liga `/docs`, `/redoc` e `/openapi.json`. **Desligue em produção** |
| `REQUEST_TIMEOUT_SECONDS` | <valor> | Tempo máximo da requisição |
| `RATE_LIMIT` | `60/minute` | Requisições por IP. Com várias instâncias, use Redis (`app/core/limiter.py`) |
| <outras> | <padrão> | <descrição> |

---

## 10. Segurança e LGPD

- Dados sensíveis vão no corpo (POST), nunca na URL.
- Logs em JSON, sem dados pessoais e sem segredos.
- Segredos só no `.env` ou em variáveis de ambiente (`SecretStr` na configuração).
- <Dados pessoais tratados, máscaras aplicadas na resposta, base legal, direito de revisão de decisões
  automatizadas (LGPD, art. 20), se aplicável.>

---

## 11. Limitações

<O que a API não faz, atrasos dos dados, casos não cobertos, dependências externas. Seja explícito: é o que evita
uso errado.>

---

## 12. Solução de problemas

| Sintoma | Causa provável | Solução |
|---|---|---|
| A API não sobe: `ValidationError` em `Settings` | `API_KEY` vazia ou curta | Preencher no `.env` |
| `401` em todas as chamadas | Chave errada ou header ausente | Header `X-API-Key` com o valor de `API_KEY` |
| `400 Invalid host header` | Host fora de `ALLOWED_HOSTS` | Incluir o host dos clientes |
| `pip-audit` aponta vulnerabilidade | Dependência com falha conhecida | Atualizar a versão no `pyproject.toml` e rodar `uv lock` |
| <outros> | <causa> | <solução> |

Ao abrir chamado, informe o **`request_id`** (ou o header `X-Request-ID`), a data e a hora.
