## Pontos a confirmar antes de gerar código

- **Versão do Python e do .NET:** o documento usa Python 3.12 e .NET 10 LTS. Confirmar se o ambiente de destino usa outra versão. O template .NET foi validado com o SDK 10.0.404 e o template Python com Python 3.12+ e uv, ambos em 11/11/2026 (ver "Situação dos templates" no SKILL.md).
- **Formato da entrada (sempre, antes dos demais): String ou JSON.** String = um único texto (template base). JSON = estrutura com vários campos (template + módulo `entrada-json`); levante os campos, tipos, obrigatórios e limites. Sem resposta do usuário: **JSON**.
- **Guardar o que a API recebeu e respondeu (só se usa banco):** auditoria e suporte, módulo `movimentacao`. Grava o corpo da entrada (CPF mascarado; outros dados pessoais como vieram): exige decidir o prazo de retenção. Para consulta a dados públicos com entrada pública, não vale o custo. Se o usuário exigir numeração sequencial por ano, o módulo (ULID) não serve. Sem resposta do usuário: não incluir.
- **Chave única ou múltiplos clientes:** muda a complexidade da autenticação nas duas linguagens.
- **Uma instância ou várias:** muda o backend do rate limit (memória ou Redis) e a estratégia de limite global.
- **Entrada usada em outro destino:** shell, SQL, caminho ou template exigem tratamento específico.
- **Nível de rigor:** API pública ou interna, o que define quais itens do CI e da observabilidade são obrigatórios.
