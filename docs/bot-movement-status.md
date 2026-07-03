# Bot PvP — Estado Atual (2026-06-28)

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

## Única limitação restante (estrutural, aceita)
- 🧱 **Faísca/dano/morte NATIVOS do bot não renderizam** — exige o bot ser `CPlayerEntity` real com
  collision-info, criada só pelo `Initialize`/**type-7** via stream reliable da sessão. Inviável
  server-side sem 2º cliente (vetado) ou a captura+handshake do Path A. Mitigado pelo recuo acima.
  Veredito completo em `docs/pvp-stage-re.md` / memória `bot-hittability-type7-verdict`.

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
