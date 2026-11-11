# Kubernetes: manifestos das APIs

Leia ao gerar ou revisar manifestos. Os templates ficam em `assets/` desta skill (Kustomize, já embutido no
`kubectl`): `base/` comum a Python e .NET e `overlays/` por ambiente. Copie para `k8s/` do projeto.

## Estrutura

```
k8s/
├── base/
│   ├── deployment.yaml     probes, recursos, segurança, preStop, /tmp
│   ├── service.yaml        ClusterIP na porta 80 -> 8080
│   ├── hpa.yaml            escala por CPU (70%), 2 a 6 réplicas
│   ├── pdb.yaml            pelo menos 1 réplica no ar durante manutenção
│   ├── networkpolicy.yaml  entrada 8080; saída DNS e bancos
│   └── kustomization.yaml
└── overlays/
    ├── local/              Docker Desktop: 1 réplica, sem HPA/PDB, banco em host.docker.internal
    └── producao/           registro e tag reais; segredos vindos do cofre
```

## Decisões e por quê

**Probes**
- `startupProbe` e `livenessProbe` em `/health/live`, que **não** consulta o banco. Se a liveness consultasse o
  banco, uma queda do banco reiniciaria todos os pods sem resolver nada.
- `readinessProbe` em `/health/ready`, que consulta o banco. Sem banco, o pod sai do Service.
  - Efeito colateral: com o banco fora, **todos** os pods saem e o cliente recebe erro do Ingress (502/503) em
    vez do `503 banco_indisponivel` da API. Se preferir a resposta da API, use `/health/live` também na
    readiness.
- **Header `Host: localhost` nas probes**: as duas APIs filtram o Host (`ALLOWED_HOSTS` / `Api__AllowedHosts`) e
  o kubelet manda o IP do pod. Sem o header, a probe recebe 400 e o pod reinicia em loop.
- **Health fora do rate limit**: as probes saem todas do IP do nó. Se o limite por IP valer para `/health/*`, um
  pico de qualquer coisa no nó faz a liveness receber 429 e o Kubernetes reinicia um pod saudável. No .NET, o
  `GlobalLimiter` pega tudo: use `.DisableRateLimiting()` em `/health/live`, `/health/ready` e `/metrics` (o template
  da criar-api já faz). No Python (slowapi), o limite fica só nas rotas decoradas.
- **Python: `/health/ready` com o banco fora demora ~5 s** (espera do pool) e a readiness tem `timeoutSeconds: 3`:
  o kubelet registra `context deadline exceeded` em vez de 503. O efeito é o mesmo (pod fora do Service). O .NET
  responde 503 na hora.

**Encerramento sem perder requisição**
- `preStop: sleep 5` (ação nativa, Kubernetes 1.30+). A imagem .NET chiseled não tem shell, então
  `exec: sleep` não funcionaria.
- Escada de tempos: banco (5 s) < requisição (10 s) < gunicorn `--timeout` (15 s) < `--graceful-timeout` (20 s)
  < `terminationGracePeriodSeconds` (40 s).

**Recursos**
- `requests` de CPU e memória: o agendador e o HPA dependem deles.
- `limits` só de memória: limite de CPU causa *throttling* (latência alta) mesmo com o nó ocioso.
- Ajuste os números com o teste de carga, não por chute.

**Segurança**
- Namespace com Pod Security `restricted`.
- `runAsNonRoot`, `readOnlyRootFilesystem` (com `emptyDir` em `/tmp`), `capabilities: drop ALL`, `seccomp
  RuntimeDefault`, `allowPrivilegeEscalation: false`, `automountServiceAccountToken: false`.
- **Segredos**: Secret separado da configuração.
  - Local: `secretGenerator` lendo `segredos.env`, arquivo que nunca é versionado.
  - Produção: cofre do ambiente (External Secrets Operator, Sealed Secrets). Um Secret é só base64, não é
    criptografia.
  - Só as variáveis que a API usa: nunca credenciais de administrador do banco.
- `/metrics` sem autenticação: nunca exponha pelo Ingress.

**Banco de dados: o limite que o cluster não resolve**
- Cada pod abre o próprio pool. Conexões no banco = réplicas × processos por pod × pool máximo.
- A soma de todas as APIs e réplicas (use o `maxReplicas` do HPA) precisa ficar abaixo do `max_connections`
  do PostgreSQL (100 por padrão) ou do limite de sessões do Oracle, com folga para cargas e administração.
- Acima disso, use PgBouncer (PostgreSQL) ou DRCP (Oracle), ou aumente o banco. Mais pods não deixam o banco
  mais rápido.

**Rate limit**
- O limite das APIs fica em memória **por pod**: com N réplicas, o limite real é N vezes maior.
- Para um limite global: no Ingress/API Gateway (ex.: anotações do ingress-nginx) ou com Redis
  (`app/core/limiter.py` no Python).

**Python: 1 processo por pod**
- `WEB_CONCURRENCY=1` e o HPA escala os pods.
- Vários workers do gunicorn por pod multiplicam pools e limites sem o cluster saber.

## Exposição externa

O template não traz Ingress: depende do controlador do cluster (ingress-nginx, Traefik, Gateway API, AGIC,
AWS Load Balancer Controller). Ao criar:
- TLS obrigatório.
- Limite de tamanho do corpo igual ou menor que o da API.
- Timeout do proxy maior que a escada da aplicação.
- Rota só para `/api/*`: nem `/metrics` nem `/docs`.

## Notas por nuvem (genérico por padrão)

| Ambiente | Registro | Segredos | Observações |
|---|---|---|---|
| Azure (AKS) | ACR, com `az aks update --attach-acr` | Key Vault + CSI driver ou External Secrets | NetworkPolicy exige Azure CNI/Calico/Cilium |
| AWS (EKS) | ECR | Secrets Manager + External Secrets | Banco RDS: Security Group liberando os nós/pods |
| Google (GKE) | Artifact Registry | Secret Manager + External Secrets | Autopilot exige `requests` em todos os containers |
| On-premise (k3s, Rancher, OpenShift) | Harbor ou registro próprio | Vault ou Sealed Secrets | OpenShift usa UID aleatório: não fixe `runAsUser` |

## Verificações

```powershell
kubectl kustomize k8s/overlays/local                    # gera o YAML final (confere o que será aplicado)
kubectl apply -k k8s/overlays/local --dry-run=server    # o cluster valida sem aplicar
docker run --rm -v "${PWD}/k8s:/k8s" ghcr.io/yannh/kubeconform:v0.6.7 -strict -summary -ignore-filename-pattern "kustomization|\.env" /k8s/base
```
