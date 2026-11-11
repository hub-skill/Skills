# Validação local: Kubernetes do Docker Desktop no Windows

Leia antes de validar os manifestos na máquina do usuário. O Docker Desktop e o acesso ao banco do Windows estão na
skill **docker** (`references/windows-docker-desktop.md`); a imagem precisa ter passado pelas verificações de lá e o
`.env.docker` precisa existir.

Roteiro executado em 11/11/2026 (Docker Desktop 29.9.3, Kubernetes v1.36.1 em modo **kind**) com os dois Motores
de Crédito; os resultados estão no SKILL.md.

## Ligar o Kubernetes (o usuário faz)

Docker Desktop → **Settings → Kubernetes → Enable Kubernetes** → **Apply & restart**. Esperar o ícone verde.

```powershell
kubectl config current-context    # docker-desktop
kubectl get nodes                 # 1 nó Ready
```

**Descubra o modo pelo nome do nó**, porque ele muda como a imagem chega ao cluster:

| Nó | Modo | Imagem do `docker build` |
|---|---|---|
| `docker-desktop` | kubeadm | Já visível ao cluster |
| `desktop-control-plane` | kind | **Não** visível: importe no nó (abaixo) |

## Levar a imagem ao cluster no modo kind

O nó é um container oculto: `docker exec desktop-control-plane ...` funciona, mas `docker cp` para ele **não** (o
arquivo não chega) e o `kind` não precisa estar instalado. Passe a imagem por pipe **no bash** (o pipe do
PowerShell 5.1 trata a saída como texto e corrompe o tar):

```bash
docker save projeto-api:0.1.0 | docker exec -i desktop-control-plane ctr -n k8s.io images import --all-platforms -
docker exec desktop-control-plane crictl images | grep projeto-api
```

- `--all-platforms`: o Docker Desktop usa o *containerd image store* e salva índice OCI com atestações; sem a opção
  a importação pode falhar.
- Ao mudar o código, gere **tag nova** (`0.1.1`), importe e troque o `newTag` do overlay. Reaproveitar a mesma tag
  com `IfNotPresent` mantém a imagem antiga no nó.

## Banco no Windows, API no pod

O banco do Windows fica em **`host.docker.internal`** também dentro do pod (no kind, o DNS resolve e as portas
5432/1521 respondem). Teste antes do deploy, num namespace sem Pod Security `restricted` (o busybox roda como
root):

```powershell
kubectl run teste-rede -n default --rm -i --restart=Never --image=busybox:1.37 -- sh -c "nc -zv -w 3 host.docker.internal 5432; nc -zv -w 3 host.docker.internal 1521"
```

## Roteiro de validação

```powershell
# 0. gerar k8s/ (templates + config.env/segredos.env a partir do .env.docker, sem imprimir valores)
& "$HOME\.claude\skills\kubernetes\scripts\preparar-k8s.ps1" -Projeto . -Nome projeto-api -Linguagem python   # ou dotnet
git check-ignore k8s/overlays/local/segredos.env    # precisa responder o caminho

# 1. manifestos
kubectl kustomize k8s/overlays/local
docker run --rm -v "${PWD}\k8s:/k8s" ghcr.io/yannh/kubeconform:v0.6.7 -strict -summary -ignore-filename-pattern "kustomization|\.env|\.gitignore" /k8s/base
kubectl apply -f k8s/overlays/local/namespace.yaml        # sem isso o dry-run abaixo dá "namespaces ... not found"
kubectl apply -k k8s/overlays/local --dry-run=server        # e nenhum aviso de Pod Security

# 2. cluster
kubectl apply -k k8s/overlays/local
kubectl -n projeto-api rollout status deploy/projeto-api --timeout=180s
kubectl -n projeto-api get pods                     # READY 1/1, RESTARTS 0
Start-Process kubectl -ArgumentList '-n','projeto-api','port-forward','svc/projeto-api','9081:80' -WindowStyle Hidden -PassThru
#  - POST real com X-API-Key (lida do segredos.env para uma variável, sem imprimir) -> 200; sem chave -> 401
#  - kubectl -n projeto-api get pod -o jsonpath='{.items[0].spec.containers[0].securityContext}' -> drop ALL, read-only
```

### 3. Troca sem queda (rode no bash: o PowerShell estraga as aspas do `sh -c`)

O `port-forward` liga-se a **um** pod e morre quando ele é substituído: meça de dentro do cluster, pelo Service.

```bash
S='ok=0; f=0; end=$(( $(date +%s) + 75 )); while [ $(date +%s) -lt $end ]; do u=http://projeto-api.projeto-api.svc.cluster.local/health/live; if out=$(wget -q -T 3 -O /dev/null $u 2>&1); then ok=$((ok+1)); else f=$((f+1)); echo "FALHA $(date +%T) $out"; fi; sleep 0.2; done; echo "RESULTADO ok=$ok falhas=$f"'
kubectl run carga -n default --restart=Never --image=busybox:1.37 -- sh -c "$S"
kubectl wait -n default --for=condition=Ready pod/carga --timeout=60s; sleep 8
kubectl -n projeto-api rollout restart deploy/projeto-api
kubectl -n projeto-api rollout status deploy/projeto-api --timeout=180s
kubectl wait -n default --for=jsonpath='{.status.phase}'=Succeeded pod/carga --timeout=120s
kubectl logs -n default carga | tail -5      # RESULTADO ... falhas=0
kubectl delete pod -n default carga
```

Se as falhas forem `429 Too Many Requests`, não é queda: o rate limit está pegando o health (corrija na API; ver
`references/manifestos.md`).

### 4. Banco inacessível

```bash
kubectl -n projeto-api set env deploy/projeto-api PG_PORT=1      # .NET: Bancos__Postgres__Porta=1
sleep 50
kubectl -n projeto-api get pods     # novo 0/1 Running RESTARTS 0; antigo 1/1 continua atendendo
kubectl -n projeto-api get events --field-selector reason=Unhealthy | tail -3
kubectl -n projeto-api rollout undo deploy/projeto-api
kubectl apply -k k8s/overlays/local  # realinha a anotação last-applied depois do undo
```

Esperado: com `maxUnavailable: 0`, a versão sem banco nunca fica pronta e o Service segue no pod antigo; a liveness
não depende do banco, então ninguém é reiniciado.

## Particularidades do Docker Desktop

| Ponto | Efeito | O que fazer |
|---|---|---|
| Modo **kind** | O cluster não vê as imagens locais | Importar no nó (acima) ou usar um registro local |
| Sem **metrics-server** | HPA fica com `<unknown>` | O overlay `local` remove o HPA. Para testar o HPA, instale o metrics-server com `--kubelet-insecure-tls` |
| **NetworkPolicy** não é aplicada | O objeto é aceito, mas não bloqueia nada | Valide o bloqueio num cluster com Calico/Cilium |
| Um nó só | `topologySpreadConstraints` e PDB não têm efeito real | Normal localmente |
| CPU e RAM compartilhadas com o Windows | Teste de carga local não mede a capacidade de produção | Use para validar o funcionamento e o comportamento, não o número final |
| Imagem .NET chiseled | `kubectl exec ... id`/`sh` falham (não existem na imagem) | Normal; diagnostique por logs, eventos e um pod auxiliar |

## Limpar

```powershell
kubectl delete -k k8s/overlays/local     # remove também o namespace
```
