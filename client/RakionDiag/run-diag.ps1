# Abre o RakionLauncher com o hook de DIAGNÓSTICO armado (dev-only RE de interoperabilidade).
# Seta RAKION_DIAG_DLL no PROCESSO do launcher -> ele injeta a DLL em CADA cliente que subir, no
# launch suspenso (antes do anti-tamper). Suba os DOIS clientes normalmente e entrem no MESMO stage.
# O hook grava, em C:\temp\addremote_hook_<PID>.log, o (seat, blobLen, blob) toda vez que o cliente
# cria um combatente remoto (AddRemotePlayer) — o insumo p/ sintetizar a mensagem server-side.
$ErrorActionPreference = "Stop"
$dll = Join-Path $PSScriptRoot "addremote_hook.dll"
if (-not (Test-Path $dll)) { throw "addremote_hook.dll ausente — rode build.ps1 primeiro" }

$launcher = Resolve-Path (Join-Path $PSScriptRoot "..\RakionLauncher\bin\Release\net9.0-windows\RakionLauncher.exe")
$env:RAKION_DIAG_DLL = $dll
Write-Host "RAKION_DIAG_DLL = $dll"
Write-Host "Abrindo launcher: $launcher"
Write-Host "Logs do hook: C:\temp\addremote_hook_<PID>.log   |   launcher: %TEMP%\rakion_launcher.log"
& $launcher
