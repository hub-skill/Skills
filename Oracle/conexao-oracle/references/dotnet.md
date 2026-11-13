# Oracle em C# (.NET): ODP.NET gerenciado

Para APIs C# (ASP.NET Core) que consultam Oracle (use o banco plugável, ex.: `localhost:1521/XEPDB1`, nunca o CDB). O código pronto
(string de conexão, tradução das falhas do driver em 503/504 inclusive `ORA-50201`, health check, repositórios com sinônimos) está no módulo
`bancos` da skill **criar-api** (`assets/dotnet-opcional/bancos/`, com `LEIAME.md`). Aqui ficam as lições do driver.

- Drivers de banco podem trazer dependências transitivas vulneráveis (ex.: `Oracle.ManagedDataAccess.Core` traz `System.Security.Cryptography.Pkcs` 8.0.1). Com `TreatWarningsAsErrors`, o build falha com NU1902/NU1903. Corrija fixando a versão no `Directory.Packages.props` com `<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>` e um `PackageVersion` corrigido; nunca desligue o aviso.
- Testes que mexem na estrutura do banco (DDL) e testes que gravam nas mesmas tabelas não podem rodar em paralelo: o Oracle responde `ORA-00054` (recurso ocupado). Ponha as duas classes numa mesma `[Collection]`.
- Instantes no Oracle: use coluna `TIMESTAMP WITH TIME ZONE` (ver a skill **oracle-naming-sinonimos**) e leia com `GetFieldValue<DateTimeOffset>`. Com `TIMESTAMP` sem fuso lido por `GetDateTime`, a API em container (UTC) devolve a hora do banco como se fosse UTC: 3 horas de erro em Brasília, sem nenhuma falha. No PostgreSQL, `timestamptz` e `GetFieldValue<DateTimeOffset>` já fazem o certo.
- Senha de usuário Oracle: no máximo 30 caracteres (`ORA-00972`); ver a seção de credenciais em `SKILL.md`.
- Teste de permissão: negado = `ORA-00942` ou `ORA-01031`.
