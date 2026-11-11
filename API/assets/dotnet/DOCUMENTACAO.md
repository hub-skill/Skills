<!--
MODELO da documentação de uso. Ao gerar a API:
  1. Substitua todo <marcador> pelo conteúdo real e apague estes comentários.
  2. Use exemplos reais (requisições e respostas obtidas rodando a API), nunca inventados.
  3. Apague as seções que não se aplicam (ex.: "Operação" se a API não tiver carga ou ferramenta).
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

Pré-requisitos: .NET SDK 10<, banco X acessível> e o arquivo `.env` preenchido (ver [Configuração](#9-configuração)).

```powershell
cd <pasta da API>
dotnet run --project src/<Projeto>.Api --urls http://127.0.0.1:5080
```

Primeira chamada (PowerShell), lendo a chave do `.env`:

```powershell
$chave = ((Get-Content .env | Where-Object { $_ -like 'Api__ApiKey=*' }) -replace '^Api__ApiKey=','')
Invoke-RestMethod -Method Post http://127.0.0.1:5080/api/v1/<rota> `
  -Headers @{ 'X-API-Key' = $chave } -ContentType 'application/json' `
  -Body '<corpo de exemplo>'
```

Resultado esperado: <o que deve voltar>.

---

## 4. Referência da API

### Autenticação

Toda chamada (exceto health checks e documentação) exige o header `X-API-Key: <valor de Api__ApiKey>`.
A chave é comparada em tempo constante. Sem chave ou com chave errada: **401**.

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
| `GET /openapi/v1.json` | não | Contrato OpenAPI. Só com `Api__EnableDocs=true` |
| `GET /scalar` | não | Documentação interativa. Só com `Api__EnableDocs=true` |

### Limites

| Limite | Valor padrão | Configuração |
|---|---|---|
| Requisições por IP | 60 por minuto | `Api__RateLimitPorMinuto` |
| Tempo total da requisição | <segundos> s | `Api__RequestTimeoutSeconds` |
| Tamanho do corpo | <tamanho> | fixo |

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

Formato único (RFC 7807 – ProblemDetails), sempre com `code` e `request_id`:

```json
{ "title": "Dados de entrada inválidos.", "status": 422, "code": "entrada_invalida", "request_id": "..." }
```

| HTTP | `code` | Quando | O cliente deve |
|---|---|---|---|
| 400 | `requisicao_invalida` | JSON malformado | Corrigir o corpo |
| 401 | `http_401` | Sem `X-API-Key` ou chave errada | Conferir a chave |
| 404 | `http_404` | Rota inexistente (com chave válida) | Conferir a URL |
| 413 | `corpo_muito_grande` | Corpo acima do limite | Reduzir o corpo |
| 422 | `entrada_invalida` | Entrada inválida | Corrigir; **não repetir** |
| 429 | `http_429` | Limite de requisições | Esperar `Retry-After` |
| 500 | `erro_interno` | Erro inesperado | Informar o `request_id` |
| 504 | `timeout` | Processamento lento demais | Tentar de novo depois |
| <outros> | <code> | <quando> | <ação> |

As mensagens de erro nunca repetem a entrada nem detalhes internos. Sem chave, até rotas inexistentes respondem
401. Repita só 429, 503 e 504, com espera crescente; nunca repita erros de entrada (400, 413, 422).

---

## 7. Exemplos de integração

### curl

```bash
curl -X POST http://127.0.0.1:5080/api/v1/<rota> \
  -H "X-API-Key: $API_KEY" -H "Content-Type: application/json" \
  -d '<corpo de exemplo>'
```

### C# (.NET)

```csharp
using System.Net.Http.Json;

var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5080"), Timeout = TimeSpan.FromSeconds(15) };
http.DefaultRequestHeaders.Add("X-API-Key", Environment.GetEnvironmentVariable("API_KEY"));
var resposta = await http.PostAsJsonAsync("/api/v1/<rota>", new { <campos> });
resposta.EnsureSuccessStatusCode();
```

<Como a API se encaixa no fluxo do sistema cliente (o que fazer com cada resultado).>

---

## 8. Operação

<Tarefas de quem opera: cargas de dados, agendamentos, migrações, ferramentas de console, monitoramento.
Apague a seção se a API não tiver nada disso.>

---

## 9. Configuração

Arquivo `.env` na raiz da API (**nunca versionado**). Variáveis de ambiente já definidas têm prioridade.
`__` separa seções: `Api__ApiKey` equivale a `Api:ApiKey`.

| Variável | Padrão | Descrição |
|---|---|---|
| `Api__ApiKey` | – (obrigatória) | Chave dos clientes, mínimo 32 caracteres. A API não sobe sem ela |
| `Api__AllowedHosts__0`... | `localhost`, `127.0.0.1` | Hosts aceitos |
| `Api__AllowedOrigins__0`... | nenhum | Origens liberadas para CORS |
| `Api__EnableDocs` | `false` | Liga a documentação interativa. **Desligue em produção** |
| `Api__RequestTimeoutSeconds` | <valor> | Tempo máximo da requisição |
| `Api__RateLimitPorMinuto` | `60` | Requisições por IP por minuto |
| <outras> | <padrão> | <descrição> |

---

## 10. Segurança e LGPD

- Dados sensíveis vão no corpo (POST), nunca na URL.
- Logs sem dados pessoais e sem segredos.
- Segredos só no `.env` ou em variáveis de ambiente.
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
| A API não sobe: erro de validação de `ApiSettings` | `Api__ApiKey` vazia ou curta | Preencher no `.env` |
| `401` em todas as chamadas | Chave errada ou header ausente | Header `X-API-Key` com o valor de `Api__ApiKey` |
| `400` / host recusado | Header `Host` fora de `Api__AllowedHosts` | Incluir o host dos clientes |
| Build falha com `NU1902`/`NU1903` | Pacote com vulnerabilidade conhecida | Atualizar a versão no `Directory.Packages.props` |
| <outros> | <causa> | <solução> |

Ao abrir chamado, informe o **`request_id`** (ou o header `X-Request-ID`), a data e a hora.
