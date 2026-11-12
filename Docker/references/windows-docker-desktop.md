# Docker Desktop no Windows com o banco no próprio Windows

Leia antes de instalar, configurar ou validar imagens na máquina do usuário.

## Instalação (o usuário faz: precisa de administrador e reinício)

1. Virtualização ligada na BIOS (o Gerenciador de Tarefas → Desempenho → CPU mostra "Virtualização: Habilitado").
2. PowerShell **como administrador**: `wsl --install --no-distribution` e reiniciar o Windows. O WSL 2 é o que
   roda os containers Linux; não é preciso instalar Ubuntu para o Docker Desktop.
3. Docker Desktop: `winget install -e --id Docker.DockerDesktop` ou o instalador do site (**AMD64** em processador
   Intel/AMD). No instalador: **Per-user** e **WSL 2** (não marque Windows Containers). Reinicie se pedir.
4. Ao abrir: aceitar os termos; o login é opcional (*Skip*). Esperar **"Engine running"**.
5. Com pouca RAM (ex.: 12 GB), limitar o WSL em `%UserProfile%\.wslconfig`:
   ```
   [wsl2]
   memory=6GB
   processors=3
   ```
   Depois: `wsl --shutdown` e abrir o Docker Desktop de novo.

Conferir: `docker version` (precisa mostrar a seção Server).

**Daemon desligado** (`docker version` mostra só o Client, ou `docker ps` diz que não achou
`//./pipe/dockerDesktopLinuxEngine`): o Docker Desktop está fechado. Abra-o pelo executável
(`%LocalAppData%\Programs\DockerDesktop\Docker Desktop.exe`, ou o atalho) e espere `docker version` mostrar o Server
(na validação de 12/11/2026 levou ~10 s; o WSL aparece como `Stopped` em `wsl -l -v` enquanto isso). Avise o usuário de que
você ligou o Docker Desktop e de que ele continua rodando.

GPU: `docker run --gpus` só funciona com placa **NVIDIA**. GPU Intel/AMD integrada não acelera containers, e APIs
que esperam o banco (I/O) não ganham nada com GPU.

## Banco no Windows, API no container

De dentro do container, `localhost` é o próprio container. O banco do Windows fica em **`host.docker.internal`**
(192.168.65.254 no Docker Desktop 29.9.3).

Na validação de 11/11/2026 (Docker Desktop 29.9.3, PostgreSQL e Oracle XE instalados no Windows com a
configuração padrão), a API no container conectou nos dois bancos **sem mudar** `pg_hba.conf`, `listen_addresses`,
listener nem firewall. Os ajustes abaixo só são necessários se o teste de rede ou a API falharem.

Teste a rede antes de culpar a API:

```powershell
docker run --rm busybox:1.37 sh -c "nc -zv -w 3 host.docker.internal 5432; nc -zv -w 3 host.docker.internal 1521"
docker run --rm postgres:18 pg_isready -h host.docker.internal -p 5432
```

**PostgreSQL**
- `listen_addresses` precisa incluir a interface usada pelo Docker. `'*'` em ambiente de estudo; em produção,
  restrinja.
- `pg_hba.conf` precisa de uma linha para a rede do Docker/WSL, com `scram-sha-256` e o usuário da aplicação:
  ```
  host  motor_credito  motor_app  192.168.65.0/24  scram-sha-256
  ```
  - A faixa varia. Descubra pelo log do PostgreSQL: o erro `no pg_hba.conf entry for host "x.x.x.x"` mostra o
    IP de origem.
  - Recarregue com `select pg_reload_conf()`.
- O Firewall do Windows pode bloquear a 5432 vinda da rede virtual do WSL.

**Oracle XE**
- O listener precisa escutar num endereço acessível pela rede do Docker, não só em `127.0.0.1`. Confira com
  `lsnrctl status`.
- DSN dentro do container: `host.docker.internal:1521/XEPDB1`.

## Roteiro de validação

```powershell
# 1. imagem
docker build -t projeto-api:0.1.0 .
Get-Content Dockerfile -Raw | docker run --rm -i hadolint/hadolint:v2.12.0
docker run --rm -v //var/run/docker.sock:/var/run/docker.sock -v trivy-cache:/root/.cache aquasec/trivy:0.58.1 image --quiet --severity HIGH,CRITICAL --ignore-unfixed projeto-api:0.1.0

# 2. variáveis (ver references/imagens.md: .env.docker sem administradores, no .gitignore)

# 3. rodar e chamar
docker run -d --name api -p 8080:8080 --env-file .env.docker projeto-api:0.1.0
curl.exe -s http://localhost:8080/health/ready
docker logs api --tail 20
#  - POST real com X-API-Key -> 200 igual ao de fora do container; sem chave -> 401

# 4. banco inacessível (porta sem servidor)
docker run -d --name api-semdb -p 8085:8080 --env-file .env.docker -e PG_PORT=1 projeto-api:0.1.0   # .NET: -e Bancos__Postgres__Porta=1
#  - /health/live 200, /health/ready 503, POST -> 503 banco_indisponivel, docker ps mostra "Up"

# 5. limpar
docker rm -f api api-semdb
```

Ao ler a chave de API do `.env.docker` para testar, guarde-a numa variável do PowerShell sem imprimir.
