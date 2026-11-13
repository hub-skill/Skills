# PostgreSQL em C# (.NET): Npgsql

Para APIs C# (ASP.NET Core) que consultam PostgreSQL. O código pronto (strings de conexão sem senha em log, sessão somente leitura com
`statement_timeout`, tradução das falhas do driver em 503/504, health check) está no módulo `bancos` da skill **criar-api**
(`assets/dotnet-opcional/bancos/`, com `LEIAME.md`). Aqui ficam as lições do driver.

- Npgsql 10 em container Linux (imagem chiseled): o padrão `GssEncryptionMode=Prefer` tenta carregar a `libgssapi_krb5.so.2`, que não existe, e registra `Cannot load library libgssapi_krb5.so.2`. Sem Kerberos, use `GssEncryptionMode.Disable` (já no módulo `bancos`; revalidado com os 67 testes em 11/11/2026).
- Carga em massa com o Npgsql (`BeginBinaryImportAsync`): descarte o importador (bloco `await using (...) { ... }`, depois do `CompleteAsync`) **antes do `CommitAsync`**. Com o importador declarado no escopo do método, o commit roda com a conexão ainda em estado de cópia e falha com `NpgsqlOperationInProgressException: The connection is already in state 'Copy'`.
- Sessão da API somente leitura (`default_transaction_read_only`): as rotinas que gravam (movimentação) abrem a própria transação com `set transaction read write`. Teste de permissão: negado = `42501`; `INSERT` permitido sem a linha-pai falha na chave estrangeira (`23503`).
- Use `timestamptz` para instantes e leia com `GetFieldValue<DateTimeOffset>`.
