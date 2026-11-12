# Módulo opcional: PostgreSQL e Oracle

Use quando a API consulta PostgreSQL (Npgsql) e/ou Oracle (ODP.NET gerenciado). Extraído e validado no
Motor-de-Credito-Net, com os dois bancos reais. Garante que **falha de banco nunca vira erro 500**.

## O que traz

| Arquivo | Conteúdo |
|---|---|
| `src/Bancos/ErrosDeBanco.cs` | `BancoIndisponivelException` (503) e `BancoExpirouException` (504) |
| `src/Bancos/Postgres.cs` | `ConexoesPostgres.Montar` (read only + `statement_timeout` para leitura) e `ErrosPostgres.TraduzirAsync` |
| `src/Bancos/Oracle.cs` | `ConexoesOracle.Montar` e `ErrosOracle.TraduzirAsync` (inclui o `ORA-50201` do ODP.NET) |
| `src/Bancos/BancoHealthCheck.cs` | `/health/ready` indisponível quando o banco falha |
| `src/Bancos/RespostaDeSaudeComBanco.cs` | Corpo do `/health/ready` com banco: `{"status":"ok","banco":"postgres"}` e, com o banco fora, 503 `{"status":"banco_indisponivel"}` |
| `tests/FalhasDeBancoTests.cs`, `tests/RespostaDeSaudeComBancoTests.cs` | Drivers reais contra porta sem servidor (roda no CI, sem banco), classificação dos códigos e o corpo do `/health/ready` |

Copie só o que usar: sem Oracle, apague `Oracle.cs` e os testes de Oracle; sem PostgreSQL, o inverso.

## Como incluir

1. Copie `src/Bancos/` para `src/ProjetoApi/Bancos/` e `tests/FalhasDeBancoTests.cs` e `tests/RespostaDeSaudeComBancoTests.cs` para
   `tests/ProjetoApi.UnitTests/`.
2. Em `src/ProjetoApi/ProjetoApi.csproj`, adicione os pacotes usados (as versões já estão no `Directory.Packages.props`,
   inclusive a correção da dependência vulnerável que o driver Oracle traz):

```xml
<PackageReference Include="Npgsql" />
<PackageReference Include="Oracle.ManagedDataAccess.Core" />
```

3. Em `Errors/ErroDeDominioHandler.cs`, mapeie as falhas de banco (antes do caso genérico `_ =>`):

```csharp
BancoExpirouException erro => (StatusCodes.Status504GatewayTimeout, "banco_timeout", erro.Message),
ErroDeBancoException => (StatusCodes.Status503ServiceUnavailable, "banco_indisponivel",
    "Banco de dados indisponível. Tente novamente."),
```

   e registre em log só o tipo da causa (`exception.InnerException?.GetType().Name`), nunca a exceção inteira:
   a mensagem do driver pode trazer detalhes da conexão.

4. Nos repositórios, envolva cada acesso ao banco e use sempre parâmetros (nunca concatene valores no SQL):

```csharp
public Task<IReadOnlyList<Cliente>> BuscarAsync(string documento, CancellationToken ct) =>
    ErrosPostgres.TraduzirAsync(async () =>
    {
        await using var comando = _fonte.CreateCommand("select id, nome from app.clientes where documento = $1");
        comando.Parameters.Add(new NpgsqlParameter { Value = documento });
        ...
    });
```

5. Em `Program.cs`, a fonte de conexões (com pool) e o health check do banco:

```csharp
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(ConexoesPostgres.Montar(
    host, porta, banco, usuario, senha, somenteLeitura: true)));
builder.Services.AddHealthChecks().Add(new HealthCheckRegistration("banco",
    sp => new BancoHealthCheck(ct => ErrosPostgres.TraduzirAsync(async () =>
    {
        await using var c = sp.GetRequiredService<NpgsqlDataSource>().CreateCommand("select 1");
        return await c.ExecuteScalarAsync(ct);
    })), failureStatus: null, tags: null));
```

   Com o módulo, o corpo do `/health/ready` passa a informar o banco: troque o `ResponseWriter` do `MapHealthChecks("/health/ready", ...)`
   no `Program.cs` (o do template base, `RespostaDeSaude.Pronto`, é genérico e responde só `{"status":"ok"}` / `indisponivel`):

```csharp
ResponseWriter = (contexto, relatorio) => RespostaDeSaudeComBanco.Pronto(contexto, relatorio, "postgres"), // ou "oracle"
```

   Resultado: 200 `{"status":"ok","banco":"postgres"}`; banco fora, 503 `{"status":"banco_indisponivel"}` (o mesmo nome do `code` de
   erro das chamadas de negócio). Atualize o teste `Health_e_cabecalhos_de_seguranca` e o pedido `health/ready` da coleção Postman.

6. Credenciais no `.env`/variáveis de ambiente (`Bancos__Postgres__Senha`...), com um usuário de aplicação de
   privilégio mínimo. Para o banco em si, siga as skills de nomenclatura (postgresql-naming-schemas,
   oracle-naming-sinonimos).

## Cuidados com o Oracle e com testes de banco real

- Senha de usuário Oracle: no máximo 30 caracteres (ver a skill **conexao-oracle**).
- Teste de permissão: negado = `ORA-00942`/`01031` (Oracle) e `42501` (PostgreSQL); `INSERT` permitido sem a linha-pai falha na chave
  estrangeira (`23503`). Não assuma que um valor "improvável" não existe nos dados reais.
- Dois executáveis na solução (API e ferramenta) têm cada um um `Program`: em projeto de teste que referencia os dois, use outro tipo
  público da API no `WebApplicationFactory<T>`.

## Não coberto pelos testes automáticos

O timeout real (`statement_timeout` no PostgreSQL e `ORA-01013` no Oracle) exige uma consulta lenta de verdade;
a tradução está no código, mas só é exercitada contra um banco real.
