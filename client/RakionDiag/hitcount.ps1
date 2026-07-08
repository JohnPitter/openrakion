# hitcount.ps1 — compila e roda o hook externo de AddHitCount (diagnóstico do HIT×N com o bot).
# NÃO injeta DLL (só WriteProcessMemory, que o anti-tamper permite). Rode com o jogo JÁ no stage.
#
# Uso:  .\hitcount.ps1                 # 2 humanos SÓ (baseline: bata e veja se AddHitCount é chamado)
#       .\hitcount.ps1                 # 2 humanos + bot (bata no humano E no bot; compare)
# Deixa rodando, BATA no oponente ~10s, Ctrl+C. Veja C:\temp\hitcount.log.
param(
    [string]$Vcvars = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe  = 'C:\temp\capture_hitcount.exe'

if (-not (Test-Path $Vcvars)) {
    $Vcvars = Get-ChildItem "C:\Program Files*\Microsoft Visual Studio" -Recurse -Filter vcvarsall.bat -ErrorAction SilentlyContinue |
              Select-Object -First 1 -ExpandProperty FullName
    if (-not $Vcvars) { throw "vcvarsall.bat não achado. Passe -Vcvars." }
}

Write-Host "Compilando capture_hitcount.exe (x86)..." -ForegroundColor Cyan
$cmd = "call `"$Vcvars`" x86 && cd /d `"$here`" && cl /nologo /O2 /EHsc capture_hitcount.cpp /Fe:$exe /link advapi32.lib"
& cmd.exe /c $cmd | Out-Host
if (-not (Test-Path $exe)) { throw "compilação falhou" }
Write-Host "OK -> $exe" -ForegroundColor Green

# espera o jogo estar num stage (entitiesmp desempacotado)
Write-Host "Aguardando o rakion.exe no stage... (Ctrl+C aborta)" -ForegroundColor Cyan
for ($i=0; $i -lt 600; $i++) {
    if (Get-Process rakion -ErrorAction SilentlyContinue) { break }
    Start-Sleep -Seconds 1
}
Write-Host "`n>>> BATA no oponente enquanto roda. Ctrl+C p/ parar. Depois veja C:\temp\hitcount.log <<<`n" -ForegroundColor Yellow
& $exe
