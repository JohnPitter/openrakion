# orig_restore.ps1 — desfaz orig_capture.ps1: para MITM + tira o container original + religa o stack .NET.
Write-Output "== parando MITM =="
$p = (Get-NetTCPConnection -LocalPort 41708 -State Listen -ErrorAction SilentlyContinue).OwningProcess
if($p){ Stop-Process -Id $p -Force -ErrorAction SilentlyContinue; Write-Output "   MITM parado (pid $p)" } else { Write-Output "   MITM ja parado" }

Write-Output "== removendo container original (libera portas) =="
docker rm -f rakion-cap 2>$null | Out-Null

Write-Output "== garantindo rakion-db (MariaDB do .NET) UP =="
docker start rakion-db 2>$null | Out-Null
$ok=$false; for($i=0; $i -lt 15; $i++){ $r = docker exec rakion-db mariadb -uroot -p123456 rakion -e "SELECT COUNT(*) FROM characterinfo" 2>&1; if(($r -notmatch 'ERROR') -and ($r -match '\d')){ $ok=$true; break }; Start-Sleep -Seconds 2 }
Write-Output "   rakion-db: $ok"

Write-Output "== religando stack .NET =="
& "C:\Users\joaop\Desenvolvimento\openrakion\server\RakionServer\start-stack.ps1"
Start-Sleep -Seconds 3
Get-Process -Name RakionWorldServer,BrokenServer,RakionLauncherWeb,RakionAdmin -ErrorAction SilentlyContinue | Select-Object Name,Id | Format-Table -AutoSize
Write-Output "PRONTO — stack .NET no ar."
