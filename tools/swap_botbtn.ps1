# swap_botbtn.ps1 — troca segura do cliente patcheado (botao "Add Bot"), com backup. Trata rakion.exe E rakion.bin.
#   ...swap_botbtn.ps1 apply     -> backup -> .orig, e poe o .botbtn no lugar (nos dois arquivos)
#   ...swap_botbtn.ps1 restore   -> volta os .orig
# Uso: powershell -ExecutionPolicy Bypass -File "<...>\tools\swap_botbtn.ps1" apply
param([Parameter(Mandatory=$true)][ValidateSet("apply","restore")][string]$mode)

$dir = "C:\Users\joaop\Desenvolvimento\Rakion\rakion-final\Bin"
$files = @("rakion.exe", "rakion.bin")

foreach ($f in $files) {
    $bin = Join-Path $dir $f
    $orig = "$bin.orig"
    $patched = "$bin.botbtn"
    if ($mode -eq "apply") {
        if (-not (Test-Path $patched)) { Write-Host "[skip] $f.botbtn nao existe (rode patch_botbtn.py)" -ForegroundColor Yellow; continue }
        if (-not (Test-Path $orig)) { Copy-Item $bin $orig; Write-Host "[ok] backup $f -> $f.orig" -ForegroundColor Green }
        Copy-Item $patched $bin -Force
        Write-Host "[ok] APLICADO em $f" -ForegroundColor Cyan
    } else {
        if (Test-Path $orig) { Copy-Item $orig $bin -Force; Write-Host "[ok] RESTAURADO $f" -ForegroundColor Green }
        else { Write-Host "[!] sem backup de $f" -ForegroundColor Yellow }
    }
}
if ($mode -eq "apply") { Write-Host ">> Feche o jogo se aberto, rode pelo launcher e ENTRE numa SALA. Procure o botao 'Add Bot' embaixo." -ForegroundColor Cyan }
