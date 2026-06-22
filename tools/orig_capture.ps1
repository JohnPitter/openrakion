# orig_capture.ps1 — sobe o servidor ORIGINAL (container rakion-cap, imagem openrakion-server:latest) com
# captura COMPLETA pra debug de compatibilidade do cliente offline:
#   (1) general_log do MariaDB ON  -> revela TODA query SQL do server (de qual tabela/userid/condicao ele le).
#       FOI A CHAVE: achou o box-load (UserItemInfo WHERE userid=N) e o quickslot (useriteminfo slots 13/14/15 -> 0x0c@149).
#   (2) MITM (41708->40708)         -> frames W<->C DECIFRADOS em C:\temp\mitm.log (achar offsets do 0x0c etc).
#   (3) docker logs                 -> stdout do server original (erros de Wine/GG/world).
# USO:  .\orig_capture.ps1            (para o stack .NET, sobe o original, liga os logs). Depois logue o cliente.
#       .\orig_diag.ps1               (le estado do DB + queries capturadas + docker logs).
#       .\orig_restore.ps1            (tira original+MITM, religa o stack .NET).
# Pre-req: Docker UP, imagem openrakion-server:latest, mitm_cap.py + GameServers_cap.ini nesta pasta.
param([switch]$NoMitm)
$here = $PSScriptRoot
$ini  = ($here -replace '\\', '/') + '/GameServers_cap.ini'   # forward-slash p/ o -v do docker

Write-Output "== parando stack .NET (libera portas 80/40706/40708) =="
foreach ($p in "RakionWorldServer", "BrokenServer", "RakionLauncherWeb") { try { Stop-Process -Name $p -Force -ErrorAction Stop } catch {} }
Start-Sleep -Milliseconds 500

Write-Output "== subindo ORIGINAL (rakion-cap) =="
docker rm -f rakion-cap 2>$null | Out-Null
docker run -d --name rakion-cap -e WINEARCH=win32 -p 80:80 -p 40706:40706 -p 40708:40708 -p 40708:40708/udp -p 40709:40709/udp -v "${ini}:/server/BrokenServer/Settings/GameServers.ini" openrakion-server:latest | Out-Null

$ok = $false; for ($i = 0; $i -lt 20; $i++) { $r = docker exec rakion-cap sh -lc "mariadb -uroot rakion -e 'SELECT 1' 2>/dev/null" 2>&1; if (($r -match '1') -and ($r -notmatch 'ERROR')) { $ok = $true; break }; Start-Sleep -Seconds 2 }
if (-not $ok) { Write-Output "!! DB do original NAO subiu (timeout)"; return }

Write-Output "== ligando general_log (captura toda query SQL do server) =="
docker exec rakion-cap sh -lc "mariadb -uroot rakion -e 'SET GLOBAL general_log=1'" | Out-Null
$logf = (docker exec rakion-cap sh -lc "mariadb -uroot -N rakion -e 'SELECT @@general_log_file'" 2>&1) -join ''
Write-Output "   queries -> /var/lib/mysql/$logf  (le com .\orig_diag.ps1)"

if (-not $NoMitm) {
  $py = (Get-Command python -ErrorAction SilentlyContinue).Source; if (-not $py) { $py = (Get-Command py -ErrorAction SilentlyContinue).Source }
  Start-Process -FilePath $py -ArgumentList "$here\mitm_cap.py" -WindowStyle Hidden
  Start-Sleep -Seconds 2
  Write-Output "== MITM UP (41708->40708) -> frames decifrados em C:\temp\mitm.log =="
}
Write-Output ""
Write-Output "PRONTO. Logue o cliente (test/test) no ORIGINAL. Depois rode: .\orig_diag.ps1"
