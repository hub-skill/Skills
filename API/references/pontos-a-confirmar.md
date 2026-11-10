## Pontos a confirmar antes de gerar código

- **Versão do Python e do .NET:** o documento usa Python 3.12 e .NET 10 LTS. Confirmar se o ambiente de destino usa outra versão.
- **Chave única ou múltiplos clientes:** muda a complexidade da autenticação nas duas linguagens.
- **Uma instância ou várias:** muda o backend do rate limit (memória ou Redis) e a estratégia de limite global.
- **Entrada usada em outro destino:** shell, SQL, caminho ou template exigem tratamento específico.
- **Nível de rigor:** API pública ou interna, o que define quais itens do CI e da observabilidade são obrigatórios.
