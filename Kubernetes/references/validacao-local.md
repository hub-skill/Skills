# Validação local: Kubernetes do Docker Desktop no Windows

Leia antes de validar os manifestos na máquina do usuário. O Docker Desktop e o acesso ao banco do Windows estão na
skill **docker** (`references/windows-docker-desktop.md`); a imagem precisa ter passado pelas verificações de lá.

## Ligar o Kubernetes (o usuário faz)

Docker Desktop → **Settings → Kubernetes → Enable Kubernetes**, modo **kubeadm** → **Apply & restart**. Esperar o
ícone do Kubernetes ficar verde (baixa componentes; leva alguns minutos).

Conferir:

```powershell
kubectl config current-context    # docker-desktop
kubectl get nodes                 # 1 nó Ready
```

## Particularidades do Docker Desktop

| Ponto | Efeito | O que fazer |
|---|---|---|
| Modo **kubeadm** | Imagens do `docker build` já ficam visíveis ao cluster | `imagePullPolicy: IfNotPresent` e tag fixa (nunca `latest`, que força pull) |
| Modo **kind** | O cluster não vê as imagens locais | `kind load docker-image projeto-api:0.1.0 --name desktop` ou um registro local |
| Sem **metrics-server** | HPA fica com `<unknown>` | O overlay `local` remove o HPA. Para testar o HPA, instale o metrics-server com `--kubelet-insecure-tls` |
| **NetworkPolicy** não é aplicada | O objeto é aceito, mas não bloqueia nada | Valide o bloqueio num cluster com Calico/Cilium |
| Um nó só | `topologySpreadConstraints` e PDB não têm efeito real | Normal localmente |
| CPU e RAM compartilhadas com o Windows | Teste de carga local não mede a capacidade de produção | Use para validar o funcionamento e o comportamento, não o número final |

## Banco no Windows, API no pod

Igual ao container: o banco do Windows fica em **`host.docker.internal`** (DSN Oracle
`host.docker.internal:1521/XEPDB1`). Ajustes de `pg_hba.conf` e listener, se precisar, estão na skill docker.

Teste a rede de dentro do cluster:

```powershell
kubectl run teste-rede --rm -it --restart=Never --image=busybox:1.37 -- nc -zv -w 3 host.docker.internal 5432
kubectl run teste-rede --rm -it --restart=Never --image=busybox:1.37 -- nc -zv -w 3 host.docker.internal 1521
```

## Roteiro de validação

```powershell
# 1. manifestos
kubectl kustomize k8s/overlays/local
kubectl apply -k k8s/overlays/local --dry-run=server
docker run --rm -v "${PWD}/k8s:/k8s" ghcr.io/yannh/kubeconform:v0.6.7 -strict -summary -ignore-filename-pattern "kustomization|\.env" /k8s/base

# 2. cluster
Copy-Item k8s/overlays/local/config-python.env.example k8s/overlays/local/config.env   # ou config-dotnet
# segredos.env: o usuário preenche (nunca pelo chat)
kubectl apply -k k8s/overlays/local
kubectl -n projeto-api rollout status deploy/projeto-api --timeout=120s
kubectl -n projeto-api get pods                     # READY 1/1, RESTARTS 0
kubectl -n projeto-api port-forward svc/projeto-api 8080:80

# 3. comportamento
#  - POST real com X-API-Key -> 200
#  - parar o banco -> /health/ready 503, pod sai de READY, RESTARTS continua 0 (a liveness não depende do banco)
#  - kubectl rollout restart -> nenhuma requisição falha durante a troca (preStop + maxUnavailable 0)
#  - kubectl get pod -o jsonpath='{.spec.containers[0].securityContext}' -> não root, read-only

# 4. limpar
kubectl delete -k k8s/overlays/local
```
