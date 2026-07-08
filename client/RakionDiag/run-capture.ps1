# Instala o HOOK EXTERNO em AddRemotePlayer no rakion.exe que estiver no stage (sem injetar DLL).
# Rode com o CLIENTE 1 já no stage e ANTES de o cliente 2 spawnar. Deixe rodando; Ctrl+C p/ sair
# (ele des-detoura o cliente ao sair). Capturas em C:\temp\addremote_capture.log.
param([int]$TargetPid = 0)
$ErrorActionPreference = "Stop"
$exe = Join-Path $PSScriptRoot "capture_addremote.exe"
if (-not (Test-Path $exe)) { throw "capture_addremote.exe ausente — rode build (cl) primeiro" }
if (Test-Path "C:\temp\addremote_capture.log") { Remove-Item "C:\temp\addremote_capture.log" -Force }
Write-Host "Instalando hook externo... (Ctrl+C encerra e restaura)" -ForegroundColor Cyan
if ($TargetPid -gt 0) { & $exe $TargetPid } else { & $exe }
