# Bot PvP — Estado Atual (2026-06-28; regressão corrigida 2026-07-06)

## REGRESSÃO "bot parado" (2026-07) — causa e regra permanente
Sintoma: o bot aparecia no stage (0x4b TCP) mas **não se movia**. Causa: o mini-peer
(`BotPeer.Connect`, adicionado no peer-codec de 2026-07-03 p/ o sub-projeto headless-H3) mandava
o OPEN de canal reliable (0x0304) + keepalives **do socket dedicado do bot (porta 41xxx) DIRETO ao
cliente** — pro MESMO slot que o 0x319 tinha registrado como `servidor:40708`. O cliente re-ligava
o peer do slot ao endpoint errado e o `IsValidUDP_ForPlayer` passava a REJEITAR todo 0x30a relayado
pelo servidor → bot congelado no spawn. A cadeia server-side estava íntegra (provada pelo teste de
integração `BotMovementChainTests`: 0x319 + fluxo de 0x30a com posição avançando chegam ao endpoint
do host).

**REGRA (não regredir): NENHUM pacote do bot fala DIRETO com o cliente.** O socket dedicado do bot
só envia ao servidor (loopback); quem fala com o cliente é SEMPRE o socket do `UdpGameplay` (mesma
origem do 0x319 e do eco de lockstep 0x0305). O codec `RakionServer.Peer` segue vivo p/ o headless-H3,
mas fora do caminho do bot. Fixes acompanhantes: clamp de parede = **união** do box do mapa com o
hull empírico dos humanos (preferir o hull sozinho clampava o bot pro quadradinho do spawn do humano
no início do round), e o spawn primário (`SpawnFieldBotsInStage`) usa o mesmo golden source de
posicionamento do fallback (`SpawnBotIntoRound`, com o spawn REAL do mapa no modo Golem).


> Bot server-side (sem 2º cliente) que anda, luta e joga o objetivo Golem War. Movimento via
> **0x319** (registro de endpoint UDP no cliente), NÃO headless. Combate cliente-autoritativo:
> o servidor arbitra o hit humano→bot e sintetiza a morte do bot.

## Validado IN-GAME (log do teste no mapa gravity)
- ✅ **Anda** no stage — emite 0x30a; o gate do cliente abre via 0x319 + eco lockstep 0x0305.
- ✅ **Luta e LEVA dano** server-side — o humano deu **36 acertos + 7 mortes do bot** (arbitragem
  `Field.ResolveBotHitByHuman`; melee chega por **0x0311**, dano 40, dist 1–2.6).
- ✅ **Renasce** no round (revive no lugar, sem teleporte).
- ✅ **Combos autênticos e variados** — actionIds REAIS capturados do humano: cadeia de M1
  `0x0400→0x0C00` + especiais `0x14/0x19` (pulo/skill). 5 padrões rotativos.
- ✅ **Nome** ("Rok" Lv5) renderiza (efeito de estar networkado).
- ✅ **Reação de hit VISÍVEL** — `BotPlayer.ApplyKnockback`: o bot recua ao apanhar e congela ao
  morrer (a POSIÇÃO renderiza via 0x30a mesmo sem colisão).

## Rota de objetivo Golem War (código completo; pendente confirmação in-game)
- **Calibração (FATO):** o espaço do 0x30a == o espaço do `.wld` em metros (IDENTIDADE; provado
  byte-a-byte). MapId do gravity = **210**.
- **Geometria gravity (FATO):** corredor leste-oeste, eixo Z≈0 livre de parede; golem dourado
  central `(0,−14,0)`; masters `(±40,0,0)`; spawns `(±50,0,0)`; sem teleporte.
- **Rota:** spawn (±50) → corredor (Z≈0) → golem dourado (centro) → golem INIMIGO (∓40) →
  `EndRoundObjective` = vitória do round. Implementada em `Domain/GolemWarLayout.cs`
  (`GolemWarLayouts.Gravity`) + `WorldServer.BotObjective.cs`.
- Decisão lutar-vs-objetivo: humano inimigo próximo (≤15) → luta; senão avança o objetivo.

## Combate type-7 TRAVADO por e2e (2026-07-09)
`tests/.../BotCombatChainTests.cs` dirige o `WorldServer` + `UdpGameplay` REAIS (clientes fake:
socket UDP em porta baixa + par TCP) e crava no FIO o comportamento-alvo:
- **Sem storm:** o type-7 nunca emite create de NPC (0x0307/0x8307) — o re-envio era o que gerava o
  "unknown error"; o teste conta ZERO creates enquanto o bot emite 0x30a normal.
- **Morte no fio:** golpe do humano (0x0311) → arbitragem server-side → 0x4f (vítima=bot, killer=humano)
  chega ao canal FIELD do humano.
- **Bot não mata humano server-side:** loop de combate colado; o rec do humano NUNCA vira `Dead` e nenhum
  0x4f com vítima=humano é emitido (combate cliente-autoritativo, evita o desync que travava o HIT×N).
- **Exibição de hits com o bot presente:** com 2 humanos + bot, o 0x0311 de um humano AINDA é relayado ao
  outro (a regressão "ninguém se hita com bot" não volta).
Recuo/flinch/stagger e a morte in-place já eram cobertos por `BotLifecycleTests`. 177 testes verdes.

## Única limitação restante (estrutural, aceita)
- 🧱 **Faísca/HUD de HIT×N e queda-de-morte NATIVOS não renderizam** — exige o bot ser `CPlayerEntity` real
  com collision-info, criada só pelo `Initialize`/**type-7** via stream reliable da sessão (peer de sessão
  real). Inviável server-side sem 2º cliente (vetado). Mitigado: recuo + flinch + pose parada no chão + 0x4f
  + HIT×N no chat de stage (`AnnounceStage`). Veredito completo em `docs/pvp-stage-re.md` / memórias
  `bot-hittability-type7-verdict` e `hitxn-combo-client-local-refutacoes`.

## Checklist de teste in-game (gravity)
1. Adicionar bot (`/addbot` ou botão), entrar no stage.
2. **Recuo:** bater no bot → ele cambaleia para trás a cada golpe; congela ao morrer.
3. **Rota:** ficar longe do bot → ele percorre o corredor central rumo ao golem (não trava no spawn,
   não atravessa parede).
4. **Objetivo:** o bot ataca o golem dourado e depois o golem inimigo → ao destruí-lo, o round é vencido.
5. **Combos:** o bot encadeia sequências variadas (não 1 golpe repetido).
6. Diagnóstico: `worldserver.log` loga `decisão` (LUTAR/OBJETIVO + pos + map), `combat`
   (ACERTOU/ERROU/MORREU + dist), `teleporte`/`DERROTOU golem`.

## Arquivos relevantes
- `src/RakionServer.World/WorldServer.BotAi.cs` — IA: decisão, movimento, combos, emissão.
- `src/RakionServer.World/WorldServer.BotObjective.cs` — navegação de objetivo (rota de waypoints).
- `src/RakionServer.World/Domain/GolemWarLayout.cs` — geometria por mapa (rota/spawn).
- `src/RakionServer.World/Domain/BotPlayer.cs` — estado, movimento, knockback.
- `src/RakionServer.World/Domain/Field.cs` — arbitragem hit→bot, dano ao golem, fim de round.
- `src/RakionServer.World/Network/BotMovement.cs` — codec 0x30a/0x030f/0x0311/0x0319.
- `src/RakionServer.World/Network/UdpGameplay.cs` — relay + 0x319 + arbitragem do 0x0311.
