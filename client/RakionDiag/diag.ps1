# diag.ps1 — FAZ TUDO do diagnóstico do muro do HIT×N num comando só:
#   1) compila a entitydiff.dll (x86)          2) garante Docker + MariaDB (rakion-db)
#   3) sobe a stack de servidores              4) limpa dumps antigos
#   5) abre a launcher com RAKION_DIAG_DLL      6) espera os 24 snapshots
#   7) roda o diff (se você passar os seats)
#
# Uso:  .\diag.ps1                         (roda tudo; no fim mostra o comando do diff)
#       .\diag.ps1 -HumanSeat 10 -BotSeat 11   (também roda o diff no fim)
#       .\diag.ps1 -SkipServers            (pula Docker/stack — se já estão no ar)
param(
    [int]$HumanSeat = -1,
    [int]$BotSeat   = -1,
    [switch]$SkipServers,
    [string]$Dll    = 'C:\temp\entitydiff.dll',
    [string]$DbContainer = 'rakion-db'
)
$ErrorActionPreference = 'Stop'
$here    = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo    = Resolve-Path (Join-Path $here '..\..')
$dumpDir = 'C:\temp\entdiff'

function Step($n, $msg) { Write-Host "`n[$n] $msg" -ForegroundColor Cyan }

# ---- 1) DLL ----
Step 1 "Compilando a entitydiff.dll"
& (Join-Path $here 'build.ps1') -ErrorAction Stop | Out-Host
if (-not (Test-Path $Dll)) { throw "build falhou: $Dll não existe" }

if (-not $SkipServers) {
    # ---- 2) Docker + MariaDB ----
    Step 2 "Garantindo Docker + MariaDB ($DbContainer)"
    $dockerOk = $false
    try { docker info 2>$null | Out-Null; $dockerOk = ($LASTEXITCODE -eq 0) } catch { $dockerOk = $false }
    if (-not $dockerOk) {
        Write-Host "  Docker parado — iniciando o Docker Desktop..." -ForegroundColor Yellow
        $dd = "$env:ProgramFiles\Docker\Docker\Docker Desktop.exe"
        if (Test-Path $dd) { Start-Process $dd | Out-Null }
        for ($i = 0; $i -lt 60; $i++) {
            Start-Sleep -Seconds 3
            try { docker info 2>$null | Out-Null; if ($LASTEXITCODE -eq 0) { $dockerOk = $true; break } } catch {}
        }
        if (-not $dockerOk) { throw "Docker não subiu a tempo. Abra o Docker Desktop e rode de novo (ou -SkipServers)." }
    }
    $running = (docker ps --filter "name=$DbContainer" --format "{{.Names}}") -match $DbContainer
    if (-not $running) { Write-Host "  subindo $DbContainer..." -ForegroundColor Yellow; docker start $DbContainer | Out-Null }
    Write-Host "  MariaDB OK" -ForegroundColor Green

    # ---- 3) stack de servidores ----
    Step 3 "Subindo a stack de servidores"
    & (Join-Path $repo 'server\RakionServer\start-stack.ps1') | Out-Host
    Start-Sleep -Seconds 3
} else {
    Write-Host "`n[2-3] -SkipServers: assumindo Docker/MariaDB/stack já no ar." -ForegroundColor DarkGray
}

# ---- 4) limpa dumps antigos ----
Step 4 "Limpando dumps antigos em $dumpDir"
if (Test-Path $dumpDir) { Remove-Item "$dumpDir\*" -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force $dumpDir | Out-Null

# ---- 5) launcher com a env ----
Step 5 "Abrindo a launcher com RAKION_DIAG_DLL"
$launcher = @(
    Join-Path $here '..\RakionLauncher\bin\Debug\net9.0-windows\RakionLauncher.exe'
    Join-Path $here '..\RakionLauncher\bin\Release\net9.0-windows\RakionLauncher.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $launcher) { throw "RakionLauncher.exe não achado — 'dotnet build client\RakionLauncher' antes." }
$env:RAKION_DIAG_DLL = $Dll
Write-Host "  RAKION_DIAG_DLL = $Dll" -ForegroundColor Green
Start-Process -FilePath (Resolve-Path $launcher) | Out-Null
Write-Host @"
  -> Lance o cliente HOST pela launcher (injeção automática; status 'diag: injeção precoce agendada').
     No jogo: cria sala -> 2o humano entra -> /addbot -> STAGE -> fique ~1 min (parado, andando, batendo).
"@ -ForegroundColor White

# ---- 6) espera os snapshots ----
Step 6 "Aguardando os 24 snapshots (C:\temp\entdiff\done.txt) — Ctrl+C aborta"
$deadline = (Get-Date).AddMinutes(15)
while (-not (Test-Path "$dumpDir\done.txt") -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 3 }
if (Test-Path "$dumpDir\done.txt") {
    $bins = (Get-ChildItem "$dumpDir\slot*_snap*.bin" -ErrorAction SilentlyContinue).Count
    Write-Host "  Concluído — $bins dumps." -ForegroundColor Green
    if (Test-Path "$dumpDir\entitydiff.log") { Get-Content "$dumpDir\entitydiff.log" -Tail 6 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray } }
} else {
    Write-Host "  Timeout/abort: done.txt não apareceu. Veja $dumpDir\entitydiff.log." -ForegroundColor Yellow
    return
}

# ---- 7) diff ----
Step 7 "Diff humano vs bot"
if ($HumanSeat -ge 0 -and $BotSeat -ge 0) {
    python (Join-Path $here 'diff_entities.py') $dumpDir $HumanSeat $BotSeat
} else {
    Write-Host "  Pegue os seats no worldserver.log (host=0) e rode:" -ForegroundColor Cyan
    Write-Host "    python `"$(Join-Path $here 'diff_entities.py')`" $dumpDir <seat_humano> <seat_bot>" -ForegroundColor White
    Write-Host "  (ou rode de novo: .\diag.ps1 -SkipServers -HumanSeat <h> -BotSeat <b>)" -ForegroundColor DarkGray
}
