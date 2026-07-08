# run-diag.ps1 — roda a launcher COM a env RAKION_DIAG_DLL setada, pra a injeção precoce da entitydiff.dll
# entrar nos clientes que você lançar por ela. Builda a DLL se faltar. No fim, coleta os dumps e roda o diff.
#
# Uso:  .\run-diag.ps1                       (só abre a launcher com a env; você lança os clientes)
#       .\run-diag.ps1 -HumanSeat 10 -BotSeat 11   (após o teste, também roda o diff desses seats)
param(
    [int]$HumanSeat = -1,
    [int]$BotSeat   = -1,
    [string]$Dll    = 'C:\temp\entitydiff.dll'
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

# 1) garante a DLL
if (-not (Test-Path $Dll)) {
    Write-Host "entitydiff.dll não existe — compilando..." -ForegroundColor Yellow
    & (Join-Path $here 'build.ps1')
}

# 2) limpa dumps antigos p/ o diff não misturar rounds
$dumpDir = 'C:\temp\entdiff'
if (Test-Path $dumpDir) { Remove-Item "$dumpDir\*" -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force $dumpDir | Out-Null

# 3) acha a launcher (Debug ou publish)
$launcher = @(
    Join-Path $here '..\RakionLauncher\bin\Debug\net9.0-windows\RakionLauncher.exe'
    Join-Path $here '..\RakionLauncher\bin\Release\net9.0-windows\RakionLauncher.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $launcher) { throw "RakionLauncher.exe não encontrado — rode 'dotnet build client\RakionLauncher' antes." }

# 4) seta a env (herdada pela launcher e pelos clientes que ela lançar) e abre a launcher
$env:RAKION_DIAG_DLL = $Dll
Write-Host "RAKION_DIAG_DLL = $Dll" -ForegroundColor Cyan
Write-Host "Abrindo a launcher. Lance o cliente HOST por ela (a injeção é automática no launch)." -ForegroundColor Cyan
Write-Host "No jogo: sala -> 2o humano entra -> /addbot -> STAGE -> fique ~1 min (parado, andando, batendo)." -ForegroundColor Cyan
$proc = Start-Process -FilePath (Resolve-Path $launcher) -PassThru

# 5) espera o diagnóstico terminar (a DLL escreve done.txt após 24 snapshots)
Write-Host "`nAguardando os snapshots (C:\temp\entdiff\done.txt)... Ctrl+C pra abortar." -ForegroundColor DarkGray
$deadline = (Get-Date).AddMinutes(15)
while (-not (Test-Path "$dumpDir\done.txt") -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 3 }

if (Test-Path "$dumpDir\done.txt") {
    $bins = (Get-ChildItem "$dumpDir\slot*_snap*.bin" -ErrorAction SilentlyContinue).Count
    Write-Host "Diagnóstico concluído — $bins dumps em $dumpDir." -ForegroundColor Green
    Get-Content "$dumpDir\entitydiff.log" -Tail 6 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
} else {
    Write-Host "Timeout/abort: done.txt não apareceu. Veja $dumpDir\entitydiff.log." -ForegroundColor Yellow
}

# 6) se os seats foram passados, roda o diff
if ($HumanSeat -ge 0 -and $BotSeat -ge 0) {
    Write-Host "`n== diff humano seat $HumanSeat vs bot seat $BotSeat ==" -ForegroundColor Cyan
    python (Join-Path $here 'diff_entities.py') $dumpDir $HumanSeat $BotSeat
} else {
    Write-Host "`nPegue os seats no worldserver.log e rode:" -ForegroundColor Cyan
    Write-Host "  python `"$(Join-Path $here 'diff_entities.py')`" $dumpDir <seat_humano> <seat_bot>" -ForegroundColor White
}
