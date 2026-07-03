<#
Captura LOOPBACK do canal P2P-direto da Serious Engine (UDP 2300-2399 em 127.0.0.1),
mais o relay do worldserv (UDP 40708/40709) p/ correlacao. RE de INTEROPERABILIDADE:
binario do proprio autor, servidor e cliente offline proprios; o canal P2P-direto e
loopback puro entre os 2 clientes, por isso o MITM de proxy (tools/mitm_*.py) NAO o ve.

Prova/spec do canal: Rakion\rakion-work\ghidra-proj\p2p_direct_channel_re.out.txt
  - cada engine binda UDP local 0x8fc+(lastByteIP%100) = 2300..2399 (cliente1=2301, cliente2=2302)
  - corpo reliable da sessao (TAGV/STATEDELTA/CRC/ADDPLAYER) flui nesse canal, NAO no relay 40709

Usa dumpcap (Wireshark/Npcap) na interface de loopback do Npcap. Sem Npcap+Wireshark
instalado, este script avisa exatamente o que instalar.

Uso:
  powershell -ExecutionPolicy Bypass -File tools\cap_p2p_loopback.ps1
  (Ctrl+C para parar; ou rode com -Seconds 60 p/ parar sozinho)

Saida: C:\temp\p2p_loopback.pcapng
#>
param(
  [string]$Out = "C:\temp\p2p_loopback_full.pcapng",
  [int]$Seconds = 0   # 0 = roda ate Ctrl+C
)

$ErrorActionPreference = "Stop"

function Find-Dumpcap {
  $c = Get-Command dumpcap -ErrorAction SilentlyContinue
  if ($c) { return $c.Source }
  foreach ($p in @(
      "C:\Program Files\Wireshark\dumpcap.exe",
      "C:\Program Files (x86)\Wireshark\dumpcap.exe")) {
    if (Test-Path $p) { return $p }
  }
  return $null
}

$dumpcap = Find-Dumpcap
if (-not $dumpcap) {
  Write-Host "FALTA dumpcap (Wireshark + Npcap)." -ForegroundColor Yellow
  Write-Host "Instale (uma vez):" -ForegroundColor Yellow
  Write-Host '  winget install -e --id WiresharkFoundation.Wireshark' -ForegroundColor Cyan
  Write-Host "  -> no instalador, MARQUE 'Install Npcap' e ative" -ForegroundColor Cyan
  Write-Host "     'Support loopback traffic' (Npcap Loopback Adapter)." -ForegroundColor Cyan
  Write-Host "Depois rode este script de novo." -ForegroundColor Yellow
  exit 1
}
Write-Host "dumpcap: $dumpcap"

# Acha a interface de loopback do Npcap. dumpcap -D lista as interfaces; a de loopback
# aparece como 'Adapter for loopback traffic capture' (nome NPF_Loopback / \Device\NPF_Loopback).
$ifaces = & $dumpcap -D 2>&1
Write-Host "--- interfaces ---"
$ifaces | ForEach-Object { Write-Host "  $_" }

$loop = $ifaces | Where-Object { $_ -match 'loopback' } | Select-Object -First 1
if (-not $loop) {
  Write-Host "Nenhuma interface de loopback do Npcap encontrada." -ForegroundColor Yellow
  Write-Host "Reinstale o Npcap com 'Support loopback traffic capture' MARCADO" -ForegroundColor Yellow
  Write-Host "(Npcap installer -> opcao 'Install Npcap in WinPcap API-compatible Mode'" -ForegroundColor Yellow
  Write-Host " + 'Support loopback traffic capture')." -ForegroundColor Yellow
  exit 1
}
# A linha tem o formato 'N. \Device\NPF_Loopback (Adapter for loopback...)'. Pega o indice N.
$ifIndex = ($loop -split '\.')[0].Trim()
Write-Host "loopback iface = $ifIndex  ($loop)" -ForegroundColor Green

# BPF: UDP do canal P2P (2300-2399) + relay (40708/40709) + TODO o TCP do jogo (broker 40706
# + qualquer P2P TCP entre os clientes), excluindo o ruido (web :80, launcher :8090, admin :8080,
# DB :3306). Objetivo: descartar/achar o corpo do connect (TAGV/ADDPLAYER) num canal TCP.
$bpf = "(udp and (portrange 2300-2399 or port 40708 or port 40709)) or (tcp and not port 80 and not port 8090 and not port 8080 and not port 3306)"

if (Test-Path $Out) { Remove-Item $Out -Force }

$args = @("-i", $ifIndex, "-f", $bpf, "-w", $Out)
if ($Seconds -gt 0) { $args += @("-a", "duration:$Seconds") }

Write-Host ""
Write-Host "CAPTURANDO -> $Out" -ForegroundColor Green
Write-Host "filtro: $bpf"
if ($Seconds -gt 0) { Write-Host "para sozinho em ${Seconds}s" } else { Write-Host "Ctrl+C para parar" }
Write-Host ""

& $dumpcap @args
Write-Host ""
Write-Host "Arquivo: $Out" -ForegroundColor Green
Write-Host "Analise: py tools\analyze_p2p_pcap.py `"$Out`"" -ForegroundColor Cyan
