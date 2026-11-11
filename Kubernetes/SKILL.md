---
name: kubernetes
description: Publica no Kubernetes, com Kustomize, as APIs Python (FastAPI) ou C# (.NET) já empacotadas pela skill docker. Use sempre que o usuário quiser criar ou revisar manifestos (Deployment, Service, HPA, PDB, NetworkPolicy, Namespace, Secret, ConfigMap, probes), escalar a API com réplicas, ligar e usar o Kubernetes do Docker Desktop, kind ou minikube, publicar num cluster (AKS, EKS, GKE, on-premise), ou diagnosticar pod reiniciando, CrashLoopBackOff, probe falhando, ImagePullBackOff ou pod sem acesso ao banco, mesmo que ele não cite "Kubernetes" (ex.: "escalar a api", "subir no cluster", "rodar várias réplicas"). Pressupõe uma imagem que siga o contrato da skill docker (porta 8080, /health/live e /health/ready, usuário não root). Não use para criar Dockerfile (skill docker) nem para Helm charts de terceiros.
---

# Kubernetes para as APIs

Publica as imagens feitas pela skill **docker** no Kubernetes. Os manifestos são comuns a Python e .NET porque as
duas imagens seguem o mesmo contrato:

- escutam na porta **8080**;
- têm `/health/live` (não consulta dependências) e `/health/ready` (consulta o banco);
- leem a configuração de variáveis de ambiente;
- rodam com usuário não root numérico.

Se ainda não houver imagem, use a skill **docker** primeiro.

## Passo 1: identificar a linguagem e o destino

- Linguagem: pelo projeto (`pyproject.toml` → Python; `*.csproj`/`*.slnx` → .NET). Muda só o `config.env`.
- Destino: se o usuário não souber, gere o padrão **genérico** (qualquer cluster) e o overlay `local` (Docker
  Desktop). Notas por nuvem em `references/manifestos.md`.

## Passo 2: ler as referências

- Manifestos e decisões: `references/manifestos.md`.
- Validar na máquina do usuário (Kubernetes do Docker Desktop, banco no Windows): `references/validacao-local.md`.

## Passo 3: gerar a partir dos templates

Copie `assets/` para `k8s/` do projeto (`assets/base` → `k8s/base`, `assets/overlays` → `k8s/overlays`) e troque
`projeto-api` pelo nome da API em todos os arquivos.

Ao adaptar:

1. **Imagem**: o mesmo nome e a mesma tag fixa do `docker build` (nunca `latest`).
2. **`ALLOWED_HOSTS` / `Api__AllowedHosts`**: inclua `localhost` (as probes mandam esse Host) e o nome do
   Service. Sem isso, as probes recebem 400 e o pod reinicia em loop.
3. **`config.env`**: a partir de `config-python.env.example` ou `config-dotnet.env.example`, só com
   configuração não secreta.
4. **`segredos.env`**: só as variáveis que a API usa, sem administradores. O usuário preenche os valores no
   arquivo, **nunca pelo chat**. O arquivo está no `.gitignore`.
5. **Pool × réplicas**: confira `maxReplicas` do HPA × pool máximo contra o limite de conexões do banco (conta em
   `references/manifestos.md`).
6. **Rate limit**: é por pod. Avise o usuário e, se precisar de limite global, proponha o Ingress ou Redis.

## Passo 4: verificar

Rode quando o ambiente permitir (`kubectl get nodes` mostra um nó `Ready`):

1. `kubectl kustomize` + `kubectl apply --dry-run=server` (e kubeconform).
2. `kubectl apply -k`, rollout concluído, pod `READY 1/1` com `RESTARTS 0` e chamada real pelo `port-forward`.
3. Comportamento: banco fora → readiness 503 sem reinício; `rollout restart` sem erro.

Se não conseguir executar, diga isso. Não afirme que o deploy funciona sem ter rodado.

### Situação dos templates

- **NÃO validados ainda** (11/11/2026). As imagens que eles usam foram validadas pela skill docker, mas falta ligar
  o Kubernetes no Docker Desktop e rodar o roteiro de `references/validacao-local.md` com as APIs reais (Motor de
  Crédito Python e .NET): rollout, probes com `Host: localhost`, port-forward, readiness 503, `rollout restart` e
  kubeconform. Registre aqui o resultado antes de usá-los como base sem ressalvas.

## Passo 5: entregar

Mostre:

- os arquivos gerados;
- os comandos para publicar e acompanhar;
- o resultado das verificações (ou o que não pôde ser rodado);
- os limites que o cluster não resolve: conexões no banco, rate limit por pod e NetworkPolicy, que depende do CNI.

Na API, atualize a `DOCUMENTACAO.md` (seção Operação) com a forma de publicar.
