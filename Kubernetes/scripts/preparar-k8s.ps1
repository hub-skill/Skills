# Gera k8s/ no projeto a partir dos templates desta skill e separa o .env.docker (já testado no container, ver a
# skill docker) em config.env (sem segredo) e segredos.env (no .gitignore do overlay). Nunca imprime valores.
#
# Uso:  .\preparar-k8s.ps1 -Projeto "C:\caminho\da\api" -Nome minha-api -Linguagem python|dotnet [-Tag 0.1.0]
#
# Os textos entre aspas ficam só em ASCII de propósito: o PowerShell 5.1 lê .ps1 sem BOM como ANSI e estraga os
# acentos (alguns bytes viram aspas e quebram o script). Nos comentários isso não tem efeito.
param(
    [Parameter(Mandatory)] [string] $Projeto,   # pasta do projeto (precisa ter o .env.docker)
    [Parameter(Mandatory)] [string] $Nome,      # nome da API no cluster (Deployment, Service, namespace)
    [Parameter(Mandatory)] [ValidateSet('python', 'dotnet')] [string] $Linguagem,
    [string] $Tag = '0.1.0'                     # tag da imagem já construída (a mesma do docker build)
)
$ErrorActionPreference = 'Stop'
if ($Nome -cnotmatch '^[a-z]([-a-z0-9]{0,61}[a-z0-9])?$') {
    throw "Nome recusado pelo Kubernetes: primeiro caractere a-z; depois a-z, 0-9 ou '-' (ex.: minha-api)."
}
if ($Tag -eq 'latest' -or $Tag -notmatch '^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$') {
    throw "Tag recusada: use uma tag fixa (ex.: 0.1.0), nunca latest."
}
$Projeto = (Resolve-Path $Projeto).Path          # as chamadas [IO.File] não seguem o diretório atual do PowerShell
$assets = (Resolve-Path (Join-Path $PSScriptRoot '..\assets')).Path
$k8s = Join-Path $Projeto 'k8s'
$envDocker = Join-Path $Projeto '.env.docker'
if (-not (Test-Path $envDocker)) { throw "Falta o arquivo ${envDocker}: gere-o com a skill docker antes." }

# 1) copia os templates trocando projeto-api pelo nome da API e a tag da imagem (UTF-8 sem BOM)
foreach ($f in Get-ChildItem $assets -Recurse -File -Force) {
    $rel = $f.FullName.Substring($assets.Length + 1)
    if ($rel -like 'overlays\local\*.env.example') { continue }   # config.env e o exemplo de segredos saem abaixo
    $dest = Join-Path $k8s $rel
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
    $texto = [IO.File]::ReadAllText($f.FullName) -replace 'projeto-api', $Nome
    $texto = $texto -replace '(newTag: |image: \S+:)0\.1\.0', ('${1}' + $Tag)
    [IO.File]::WriteAllText($dest, $texto)
}

# 2) separa configuração e segredos; ALLOWED_HOSTS refeito com localhost (probes) e os nomes do Service
$segredo = '(PASSWORD|PASSWD|SENHA|SECRET|TOKEN|API_?KEY|CONNECTION_?STRING|DATABASE_URL)'   # -match ignora maiúsculas
$hosts = @('localhost', $Nome, "$Nome.$Nome.svc", "$Nome.$Nome.svc.cluster.local")
$config = [Collections.Generic.List[string]]::new()
$segredos = [Collections.Generic.List[string]]::new()
foreach ($linha in Get-Content $envDocker -Encoding utf8) {
    if (-not $linha.Trim() -or $linha -match '^\s*#') { continue }
    $chave = ($linha -split '=', 2)[0]
    if ($chave -match '^(ALLOWED_HOSTS|Api__AllowedHosts__\d+)$') { continue }
    if ($chave -match $segredo) { $segredos.Add($linha) } else { $config.Add($linha) }
}
if ($Linguagem -eq 'python') {
    $config.Add('ALLOWED_HOSTS=["' + ($hosts -join '","') + '"]')
} else {
    for ($i = 0; $i -lt $hosts.Count; $i++) { $config.Add("Api__AllowedHosts__$i=$($hosts[$i])") }
}
$local = Join-Path $k8s 'overlays\local'
[IO.File]::WriteAllLines((Join-Path $local 'config.env'), [string[]]$config)
[IO.File]::WriteAllLines((Join-Path $local 'segredos.env'), [string[]]$segredos)

# 3) exemplo de segredos só com os nomes (este pode ser versionado; o segredos.env, nunca)
$exemplo = @('# Copie para segredos.env (NUNCA versionar) e preencha apenas no arquivo, nunca no chat.',
             '# Apenas o que a API usa: nada de contas de administrador.') +
           @($segredos | ForEach-Object { ($_ -split '=', 2)[0] + '=' })
[IO.File]::WriteAllLines((Join-Path $local 'segredos.env.example'), [string[]]$exemplo)

# 4) só os nomes das chaves (confira se nenhum segredo caiu no config.env)
"k8s gerado em: $k8s (imagem ${Nome}:$Tag)"
"config.env:   " + (($config | ForEach-Object { ($_ -split '=', 2)[0] }) -join ', ')
"segredos.env: " + (($segredos | ForEach-Object { ($_ -split '=', 2)[0] }) -join ', ')
