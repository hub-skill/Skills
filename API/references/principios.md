## Parte 1: Princípios comuns

Estas regras valem para qualquer linguagem.

**Contrato e validação**
- Todo campo de entrada tem limites de tamanho explícitos.
- Espaços nas extremidades são removidos antes da validação de conteúdo.
- Caracteres de controle e bytes nulos são rejeitados.
- A saída tem um contrato tipado e documentado.
- Há limite de tamanho do corpo da requisição, no proxy e na aplicação.

**Segurança**
- Autenticação por chave com comparação em tempo constante. Se houver usuários, usar OAuth2 ou JWT.
- Rate limiting por IP ou por chave, com backend compartilhado quando houver mais de uma instância.
- Limite de concorrência para não derrubar o serviço sob carga.
- CORS com origens explícitas e filtro de hosts.
- Headers de segurança: HSTS, `X-Content-Type-Options`, `X-Frame-Options`.
- Documentação desligada em produção.
- Entrada nunca concatenada em shell, SQL, caminho, template ou `eval`. Usar APIs parametrizadas e escapar conforme o destino.
- Segredos apenas em variáveis de ambiente ou secret manager.
- Container com usuário não-root e imagem enxuta.
- Logs sem o conteúdo bruto da entrada e sem segredos.

**Timeout e desempenho**
- Timeout no processamento, retornando 504.
- Trabalho pesado fora da thread principal (event loop ou thread de requisição).
- Timeout explícito em chamadas externas.
- Timeouts alinhados entre aplicação, servidor e proxy, sempre com a aplicação expirando primeiro.

**Erros**
- Um único formato de erro, com código e `request_id`.
- Exceções de domínio mapeadas para status HTTP.
- Handler global para erros inesperados: 500 genérico, com stack trace apenas nos logs.
- Status usados: 400, 401, 403, 404, 413, 422, 429, 500, 504.

**Qualidade**
- Rotas finas, sem regra de negócio.
- Serviços com funções puras sempre que possível.
- Injeção de dependência.
- Configuração centralizada e tipada.
- Versionamento na rota (`/api/v1`).
- Verificações estáticas e de estilo no CI.

**Testes**
- Unitários no serviço.
- Integração no endpoint, cobrindo sucesso, vazio, tamanho máximo, unicode, entrada inválida, auth ausente ou errada, timeout, erro interno e payloads maliciosos.
- Propriedades: entradas geradas automaticamente.
- Contrato: fuzzing a partir do OpenAPI.
- Carga antes de releases.
- Cobertura mínima no CI.

**Observabilidade e deploy**
- Logs estruturados em JSON com `request_id`.
- Métricas de latência, erros e requisições por rota.
- Health checks separados em liveness e readiness.
- Container multi-stage, com servidor de produção atrás de um processo gerenciador.
- Graceful shutdown.
- Configuração por ambiente (dev, staging, prod).

**CI**
- Lint, tipos, testes com cobertura, análise estática de segurança, auditoria de dependências, build e scan da imagem.
- Falha em qualquer etapa bloqueia o merge.

---

