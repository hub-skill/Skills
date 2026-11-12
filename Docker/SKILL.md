---
name: docker
description: Empacota APIs Python (FastAPI) ou C# (.NET/ASP.NET Core) em imagens Docker e roda em container. Use sempre que o usuário quiser criar ou revisar Dockerfile ou .dockerignore, construir ou rodar a imagem (docker build/run), gerar o arquivo de variáveis do container, varrer vulnerabilidades (trivy), checar o Dockerfile (hadolint), instalar ou configurar o Docker Desktop no Windows (WSL 2, .wslconfig), ou diagnosticar API no container sem acesso ao banco do Windows (host.docker.internal), container que cai ao subir, imagem grande ou com segredo, mesmo que ele não cite "Docker" (ex.: "containerizar a api", "subir em container", "gerar a imagem"). Feita para as APIs da skill criar-api. Para publicar no Kubernetes (Deployment, probes, HPA, Kustomize), use a skill kubernetes depois desta. Não use para docker-compose de desenvolvimento sem API.
---

# Docker para as APIs

Empacota as APIs geradas pela skill **criar-api** (Python ou .NET) em imagens prontas para produção. Python e .NET
têm Dockerfiles separados (não misture). As duas imagens seguem o mesmo contrato, que a skill **kubernetes**
pressupõe:

- escutam na porta **8080**;
- têm `/health/live` (não consulta dependências) e `/health/ready` (consulta o banco);
- leem a configuração de variáveis de ambiente;
- rodam com usuário não root numérico;
- não levam segredo nenhum dentro da imagem.

## Passo 1: identificar a linguagem

Pelo projeto: `pyproject.toml` → Python; `*.csproj`/`*.slnx` → .NET. Se não houver projeto, pergunte.

## Passo 2: ler as referências

- Regras da imagem e verificações: `references/imagens.md`.
- Máquina do usuário (Windows + Docker Desktop, banco rodando no Windows): `references/windows-docker-desktop.md`.

## Passo 3: gerar a partir dos templates

| Template | Destino no projeto |
|---|---|
| `assets/python/Dockerfile` e `.dockerignore` | raiz do projeto Python |
| `assets/dotnet/Dockerfile` e `.dockerignore` | raiz da solução .NET (troque `ProjetoApi` pelo nome do projeto) |

Ao adaptar:

1. **Nome da imagem e tag fixa**: `nome-da-api:0.1.0`, nunca `latest`.
2. **.NET com vários projetos**: copie antes do restore só os `.csproj` que a API referencia; ferramentas (carga,
   migração) ficam fora pelo `.dockerignore`.
3. **Dockerfile antigo**: compare com o template e alinhe porta, usuário, `.dockerignore` e tempos, sem apagar o que
   for específico do projeto.
4. **Arquivo de variáveis do container** (`.env.docker`): gerado do `.env` **sem as credenciais de administrador**
   e com o banco em `host.docker.internal`. Coloque-o no `.gitignore` **antes** de gerar. O usuário preenche
   senhas só no `.env`, **nunca pelo chat**; ao mostrar o arquivo, mostre só os nomes das variáveis (`cut -d= -f1`).
   Cuidado ao conferir o `.env` com `grep`: um padrão amplo (`Banco` casa `Bancos__Postgres__Senha`) imprime os **valores**.
   Se algum valor aparecer na saída, gere outro antes de usá-lo.
5. **`ALLOWED_HOSTS` / `Api__AllowedHosts`**: mantenha `localhost`; o Kubernetes vai precisar dele nas probes.

## Passo 4: verificar

Rode quando o Docker estiver instalado (`docker version` responde):

1. `docker build` sem erro.
2. hadolint sem avisos; trivy `HIGH,CRITICAL` corrigíveis = zero.
3. Usuário da imagem não root; `docker history` sem segredo; no Python, `/app` só com `.venv` e `app`.
4. `docker run` com o `.env.docker`: `/health/ready` 200 e uma chamada real com `X-API-Key` igual à que roda fora
   do container; sem chave, 401.
5. Banco inacessível (porta errada por `-e`): `live` 200, `ready` 503, chamada `503 banco_indisponivel` e o
   container continua de pé.

Se não conseguir executar, diga isso. Não afirme que a imagem funciona sem ter rodado. Docker Desktop desligado não é motivo
para pular a verificação: ligue-o (ver `references/windows-docker-desktop.md`, "Daemon desligado") e avise o usuário. No Git Bash,
veja a nota sobre `MSYS_NO_PATHCONV` em `references/imagens.md` antes de montar o socket no trivy.

### Situação dos templates

- **Validados** em 11/11/2026 com Docker Desktop 29.9.3 (WSL 2) no Windows, usando as APIs reais do Motor de
  Crédito (Python e .NET), com PostgreSQL e Oracle no Windows via `host.docker.internal`:
  - build sem erro; Python 355 MB (UID 10001); .NET 258 MB (chiseled, UID 1654);
  - hadolint v2.12.0 limpo nos dois; `docker history` sem segredos;
  - trivy 0.58.1 (`HIGH,CRITICAL`, `--ignore-unfixed`): zero nas duas. O Python só zerou com o `apt-get upgrade` na
    etapa final (antes: 60 no Debian da imagem base, nenhuma nas bibliotecas Python);
  - as 4 combinações (Python/.NET × PostgreSQL/Oracle) com `/health/ready` 200 e a mesma triagem real; sem chave,
    401; o `testar-api` do .NET passou 8/8 contra o container;
  - banco inacessível: `live` 200, `ready` 503 e `503 banco_indisponivel` (Python em ~5 s pela espera do pool;
    .NET em ~0,1 s), sem o container cair;
  - não foi preciso mexer no `pg_hba.conf` nem no listener do Oracle nessa máquina.

## Passo 5: entregar

Mostre os arquivos gerados, os comandos para construir e rodar, o resultado das verificações (ou o que não pôde ser
rodado) e o tamanho e o usuário da imagem. Na API, atualize a `DOCUMENTACAO.md` (seção Operação) com o passo a
passo do container.

Para publicar no Kubernetes, siga com a skill **kubernetes**.
