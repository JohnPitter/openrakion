# Teste OPT-IN do peer JOINER headless (docs/headless-engine-re.md §22).
#
# O muro do §21 ("opening as server, port 25600" -> ExitProcess) era do modo SERVIDOR. O JOINER (2o cliente
# que ENTRA na sessao do humano) contorna: smoke-test contra host ausente ja passa InitInternal + entra no
# JoinSession SEM o fatal. Este script aponta o joiner ao cliente HUMANO VIVO (que, em stage, ja hospeda a
# sessao e empurra o canal esperando o peer) — o teste decisivo de se o AddPlayer completa com o game-state
# vindo do host -> combatente real -> HIT×N nativo.
#
# COMO USAR:
#   1. Suba a stack e entre num stage Golem War (o de sempre) com o cliente.
#   2. No worldserver.log procure a linha:  [N] handshake UDP: endpoint 127.0.0.1:PORTA
#      (esse e' o endpoint UDP-gameplay do humano = alvo do JoinSession).
#   3. Rode:  .\join-bot-test.ps1 -Endpoint 127.0.0.1:PORTA
#   4. Observe a saida (tambem salva em join-bot-test.out.txt). O que procurar:
#        [c++] JoinSession RETORNOU OK ...         -> conectou no host humano (rung 2 OK)
#        [c++] AddPlayer_t OK -> CPlayerSource=... -> bot virou peer da sessao (rung 2 completo)
#        >>> EXCECAO no join / segfault            -> nao conectou (porta de listen != gameplay? host nao aceita join?)
#
# AVISO: e' EXPERIMENTO. Um JoinSession nao-testado contra o cliente humano vivo PODE desestabilizar o jogo
# dele. O bot type-7 (server-side, sem headless) segue o default que FUNCIONA — isto e' so a validacao do
# caminho nativo. Nao roda em producao ate o join provar-se estavel + a ponte de IA (H5) existir.

param(
    [Parameter(Mandatory = $true)][string]$Endpoint,                 # "127.0.0.1:PORTA" do worldserver.log
    [string]$World = "LevelsSV\ko2\ko2.wld",                         # mundo do stage (ko2 / gravity / ...)
    [int]$Secs = 20,
    [string]$Bin = $(if ($env:RAKION_BIN) { $env:RAKION_BIN } else { "C:\Users\joaop\Desenvolvimento\Rakion\rakion-final\Bin" })
)
$ErrorActionPreference = "Stop"
$exe = Join-Path $Bin "engine_host.exe"
if (-not (Test-Path $exe)) { throw "engine_host.exe nao encontrado em $Bin (rode build.ps1)" }
$root = Split-Path $Bin -Parent                                      # rakion-final/ = CWD (data-root da SE1)
$log  = Join-Path $PSScriptRoot "join-bot-test.out.txt"

Write-Host "JOINER -> host humano $Endpoint  (world=$World, ${Secs}s)"  -ForegroundColor Cyan
Write-Host "CWD=$root   log=$log"
Push-Location $root
try {
    & $exe $Bin "join" "Rakion" $Endpoint $World $Secs 2>&1 | Tee-Object -FilePath $log
} finally { Pop-Location }

Write-Host "`n--- linhas-chave ---" -ForegroundColor Yellow
Select-String -Path $log -Pattern "InitInternal|JoinSession|AddPlayer|RETORNOU|EXCECAO|opening as server|CPlayerSource" |
    ForEach-Object { $_.Line }
