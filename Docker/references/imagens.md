# Docker: imagens das APIs

Leia ao gerar ou revisar um Dockerfile. Os templates ficam em `assets/python/` e `assets/dotnet/`; não misture
as duas stacks.

## Regras (as duas linguagens)

| Regra | Por quê |
|---|---|
| Multi-stage: build com SDK/uv, imagem final só com o runtime | Imagem menor e sem compilador/gerenciador de pacotes para um atacante usar |
| Tags fixas (`python:3.12.13-slim`, `sdk:10.0`, `uv:0.10.8`), nunca `latest` | Build reproduzível; atualizar é decisão, não acidente |
| Dependências antes do código (`COPY pyproject.toml uv.lock` / `COPY *.csproj` + restore) | Cache: mudar o código não reinstala tudo |
| Usuário não root e **numérico** (`USER 10001`, `USER $APP_UID`) | O Kubernetes só confere `runAsNonRoot` com UID numérico |
| Porta 8080 nas duas linguagens | Mesmos manifestos Kubernetes; porta acima de 1024 não exige root |
| `.dockerignore` com `.env`, `segredos.env`, `.git`, testes | Segredo que entra no contexto de build pode ir parar numa camada da imagem |
| Segredos só em tempo de execução (variáveis de ambiente/Secret), nunca `ENV`/`ARG` com senha | `docker history` mostra `ENV` e `ARG` |
| Um processo por container | Quem escala é o orquestrador; logs vão para stdout |
| Sem `HEALTHCHECK` na imagem | Saúde é papel das probes do Kubernetes (e a imagem chiseled não tem curl) |

## Python

- Base `python:3.12.13-slim`. `psycopg[binary]` e `oracledb` (modo thin) já trazem o necessário: **não** precisa
  de `libpq` nem do Oracle Instant Client.
- `uv sync --frozen --no-dev --no-install-project`: instala exatamente o `uv.lock`, sem as dependências de
  desenvolvimento.
- `WEB_CONCURRENCY=1` (o gunicorn lê essa variável): um processo por container no Kubernetes. Fora do Kubernetes
  (VM, `docker run`), suba para o número de núcleos.
- `--worker-tmp-dir /dev/shm` e `PYTHONDONTWRITEBYTECODE=1`: funcionam com `readOnlyRootFilesystem`.
- No Linux o loop padrão do uvicorn já é compatível com o psycopg async: o `--loop asyncio:SelectorEventLoop`
  só é exigido no Windows.
- `ALLOWED_HOSTS` precisa incluir `localhost` (as probes mandam esse Host) e o nome do Service.
- `apt-get upgrade` na etapa final: a tag da imagem base fica semanas parada e acumula correções do Debian. Em
  11/11/2026, sem essa linha, o trivy acusou 60 HIGH/CRITICAL corrigíveis (todas no Debian, nenhuma nas bibliotecas
  Python); com ela, zero. O hadolint 2.12 não reclama.

## .NET

- Build com `mcr.microsoft.com/dotnet/sdk:10.0`. As imagens trazem a faixa **10.0.1xx** do SDK: o `global.json`
  precisa aceitá-la (`10.0.100` com `rollForward: latestFeature`, como no template). Fixar `10.0.4xx` sem
  `rollForward` faz o build falhar só dentro do container.
- Runtime `aspnet:10.0-noble-chiseled-extra`: sem shell e sem gerenciador de pacotes, usuário `app` (UID 1654).
  O `-extra` traz ICU e tzdata (culturas pt-BR, fusos, drivers de banco). Sem ele, use
  `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` e teste datas e textos.
- `ASPNETCORE_HTTP_PORTS=8080`. O .NET respeita os limites de CPU e memória do container sozinho.
- `Api__AllowedHosts__0=localhost` (probes) e o nome do Service nos índices seguintes.
- O `Directory.Packages.props` com a correção do `System.Security.Cryptography.Pkcs` vale também no build do
  container: o `dotnet restore` usa as mesmas versões.
- Solução com vários projetos: copie só os `.csproj` que a API referencia antes do restore e deixe ferramentas
  (carga, migração) fora pelo `.dockerignore`.
- **Npgsql 10 na chiseled**: o padrão `GssEncryptionMode=Prefer` tenta carregar a `libgssapi_krb5.so.2`, que a
  imagem não tem, e registra `Cannot load library libgssapi_krb5.so.2`. A conexão funciona, mas cada conexão perde
  uma ida e volta. Sem Kerberos, use `GssEncryptionMode = GssEncryptionMode.Disable` (já no módulo de bancos da
  criar-api).

## Verificações

```powershell
docker build -t projeto-api:0.1.0 .
# Lint do Dockerfile e varredura de vulnerabilidades sem instalar nada (rodam como containers):
Get-Content Dockerfile | docker run --rm -i hadolint/hadolint:v2.12.0
docker run --rm -v //var/run/docker.sock:/var/run/docker.sock aquasec/trivy:0.58.1 image --severity HIGH,CRITICAL --ignore-unfixed projeto-api:0.1.0
# Segredo não pode estar na imagem:
docker run --rm --entrypoint ls projeto-api:0.1.0 -A /app   # só Python (a chiseled não tem ls); esperado: .venv app
# (não use --entrypoint "": o PowerShell 5.1 descarta o argumento vazio e o Docker toma "ls" como imagem)
docker history --no-trunc projeto-api:0.1.0 | Select-String -Pattern "PASSWORD|API_KEY|SECRET"   # deve vir vazio
# Usuário:
docker inspect projeto-api:0.1.0 --format "{{.Config.User}}"   # 10001 (Python) ou o UID 1654 (.NET)
```

Rodar com um `.env.docker` gerado do `.env` local: **sem as credenciais de administrador** e com o banco em
`host.docker.internal` (dentro do container, `localhost` é o próprio container). Adicione `.env.docker` ao
`.gitignore` antes de gerar.

```powershell
# Python (troque os prefixos para o .NET: ^Admin__, Bancos__Postgres__Host, Bancos__Oracle__DataSource)
$l = Get-Content .env -Encoding utf8 | Where-Object { $_.Trim() -and $_ -notmatch '^\s*#' -and $_ -notmatch '^PG_ADMIN_|^ORACLE_ADMIN_' } |
  ForEach-Object { $_ -replace '^PG_HOST=.*','PG_HOST=host.docker.internal' -replace '^(ORACLE_DSN=)(localhost|127\.0\.0\.1)','${1}host.docker.internal' }
[IO.File]::WriteAllLines("$PWD\.env.docker", [string[]]$l)   # UTF-8 sem BOM: BOM estraga o nome da 1ª variável
docker run -d --name api -p 8080:8080 --env-file .env.docker projeto-api:0.1.0
```

- O `--env-file` não remove aspas: `SENHA="x"` chega com as aspas. Confira se o `.env` não tem valores entre aspas.
- O PowerShell 5.1 mostra acentos quebrados em JSON sem `charset` (lê como Latin-1). É só exibição: o corpo está
  em UTF-8.
