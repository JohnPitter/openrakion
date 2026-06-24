# swap_botbtn.ps1 — troca segura do rakion.bin patcheado (botao "Add Bot"), com backup automatico.
#   ...swap_botbtn.ps1 apply     -> backup rakion.bin -> rakion.bin.orig, e poe o .botbtn no lugar
#   ...swap_botbtn.ps1 restore   -> volta o rakion.bin.orig
# Uso: powershell -ExecutionPolicy Bypass -File "<...>\tools\swap_botbtn.ps1" apply
param([Parameter(Mandatory=$true)][ValidateSet("apply","restore")][string]$mode)

$bin  = "C:\Users\joaop\Desenvolvimento\Rakion\rakion-final\Bin\rakion.bin"
$orig = "$bin.orig"
$patched = "$bin.botbtn"

if ($mode -eq "apply") {
    if (-not (Test-Path $patched)) { Write-Host "[ERRO] $patched nao existe. Rode tools\patch_botbtn.py antes." -ForegroundColor Red; exit 1 }
    if (-not (Test-Path $orig)) { Copy-Item $bin $orig; Write-Host "[ok] backup -> $orig" -ForegroundColor Green }
    Copy-Item $patched $bin -Force
    Write-Host "[ok] APLICADO: rakion.bin agora tem o botao. Rode o jogo e entre numa SALA." -ForegroundColor Cyan
    Write-Host "    (se crashar/nao aparecer: ...swap_botbtn.ps1 restore  + me avise)" -ForegroundColor DarkGray
} else {
    if (Test-Path $orig) { Copy-Item $orig $bin -Force; Write-Host "[ok] RESTAURADO o rakion.bin original." -ForegroundColor Green }
    else { Write-Host "[!] sem backup ($orig). Use o launcher /fetch ou reinstale se preciso." -ForegroundColor Yellow }
}
