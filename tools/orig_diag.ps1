# orig_diag.ps1 — diagnostico do servidor ORIGINAL (rakion-cap deve estar UP via orig_capture.ps1).
#   (1) Estado do DB (char/itens/gold/ranks) com as semanticas de slot que aprendemos.
#   (2) Queries capturadas (general_log) — mostra de QUAL tabela/userid o server le cada coisa.
#   (3) Stdout do server (docker logs) — erros do Wine/GG/world.
# Aprendizados gravados na memoria [[cliente-crash-inventario-e-gameguard]].
function Q($sql){ docker exec rakion-cap sh -lc "mariadb -uroot rakion -e '$sql' 2>&1" }

Write-Output "===== ESTADO DO DB DO ORIGINAL ====="
Write-Output "-- characterinfo (potionslot = CONTAGEM de slots de pocao, nao os itens) --"
Q 'SELECT id,name,class,level,potionslot,slot FROM characterinfo'
Write-Output "-- usergameinfo (gold; NAO existe coluna cash no original) --"
Q 'SELECT id,name,charname,gold FROM usergameinfo'
Write-Output "-- useriteminfo = ITENS DO CHAR (a 'inventory' que abre no 0x2c). slot: 0-5=gear, 6-7=anel/colar,"
Write-Output "   13/14/15=QUICKSLOT (vai pro 0x0c@149/151/153 e RENDERIZA no char-select). itembox = deposito, NAO mostra aqui."
Q 'SELECT id,characterid,itemid,slot FROM useriteminfo ORDER BY slot'
Write-Output "-- itembox (deposito da conta; NAO e a inventory do char) --"
Q 'SELECT id,userid,itemid FROM itembox'
Write-Output "-- userstageinfo (ranks de stage) --"
Q 'SELECT stage,id,rank FROM userstageinfo'

Write-Output ""
Write-Output "===== QUERIES CAPTURADAS (general_log) — de onde o server le cada coisa ====="
$logf = (docker exec rakion-cap sh -lc "mariadb -uroot -N rakion -e 'SELECT @@general_log_file'" 2>&1) -join ''
docker exec rakion-cap sh -lc "grep -iE 'SELECT|INSERT|UPDATE|DELETE' /var/lib/mysql/$logf 2>/dev/null" 2>&1 | Select-Object -Last 50

Write-Output ""
Write-Output "===== STDOUT DO SERVER ORIGINAL (docker logs, ultimas linhas) ====="
docker logs --tail 30 rakion-cap 2>&1
