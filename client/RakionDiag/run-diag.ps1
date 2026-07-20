# Abre o RakionLauncher apontando o RAKION_DIR pro jogo. NÃO injeta mais DLL (o anti-tamper bloqueia
# LoadLibrary — confirmado). A captura do AddRemotePlayer é pelo HOOK EXTERNO (run-capture.ps1), que
# usa só escrita de memória (as mesmas primitivas dos patches de janela, que funcionam).
$ErrorActionPreference = "Stop"
if (-not $env:RAKION_DIR) { throw 'defina RAKION_DIR com a raiz do cliente de teste' }
$rakionExe = Join-Path $env:RAKION_DIR "Bin\rakion.exe"
if (-not (Test-Path $rakionExe)) { throw "rakion.exe nao encontrado em $rakionExe — ajuste `$env:RAKION_DIR" }

# garante que a injeção de DLL (dead-end) NÃO rode
Remove-Item Env:\RAKION_DIAG_DLL -ErrorAction SilentlyContinue

$launcher = Resolve-Path (Join-Path $PSScriptRoot "..\RakionLauncher\bin\Release\net9.0-windows\RakionLauncher.exe")
Write-Host "RAKION_DIR = $env:RAKION_DIR"
Write-Host "Abrindo launcher: $launcher"
Write-Host ""
Write-Host "FLUXO DE CAPTURA (o timing importa — o hook tem que estar ativo ANTES do 2o player spawnar):" -ForegroundColor Cyan
Write-Host "  1. Suba o CLIENTE 1, logue, crie a sala Golem War e ENTRE no stage (spawne)."
Write-Host "  2. Rode:  run-capture.ps1   (instala o hook externo no cliente 1 e fica em poll)."
Write-Host "  3. Suba o CLIENTE 2, logue, entre na MESMA sala e SPAWNE -> o cliente 1 cria o combatente"
Write-Host "     remoto -> AddRemotePlayer dispara -> consulte o log informado pelo run-capture.ps1."
Write-Host ""
& $launcher
