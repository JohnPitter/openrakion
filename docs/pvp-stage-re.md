# RE Completo do Stage PvP — Rakion (worldserv v258 + engine.dll/SE1)

> **Finalidade.** Subsídio único e definitivo para o **bot + IA 100% server-side**: descreve
> tudo que acontece dentro de um stage PvP — transporte, máquina de estado, spawn, movimento,
> combate, morte, placar, rounds, objetivos, resultado — com o opcode/`FUN_xxxx`/offset de cada
> peça. Reconstruído por RE do `worldserv.exe` (ImageBase 0x400000, Ghidra) e da `engine.dll`
> (SE1 open-source, ImageBase 0x36000000, exports C++) cruzado com captura MITM do servidor
> original. **Não é replay**: cada frame é sintetizado do estado.

Fontes no repo: [`Field.cs`](../server/RakionServer/src/RakionServer.World/Domain/Field.cs),
[`BotMovement.cs`](../server/RakionServer/src/RakionServer.World/Network/BotMovement.cs),
`WorldHandlers.ReconCombatA/B.cs`, `WorldHandlers.Generated.FieldCombat.cs`,
`WorldHandlers.ReconRoomB.cs` (0x4f), `WorldHandlers.Generated.GameResult.cs` (0x53),
[`protocol-world.md`](protocol-world.md), [`bot-movement-capture.md`](bot-movement-capture.md).

---

## 0. Modelo mental (a descoberta-chave)

**O combate é CLIENTE-AUTORITATIVO.** O servidor **não** computa hit nem dano. O fluxo é:

1. O atacante (cliente) executa o golpe; **o cliente do atacante faz a detecção de hit**
   (raycast/alcance, lógica SE1 + Rakion) e prevê o dano.
2. **A VÍTIMA (cliente) confirma o hit, decide que morreu, e reporta a PRÓPRIA morte** via
   `0x4f Die` com `[killerSeat, cause]`.
3. O servidor (`FUN_00424a20`) **arbitra**: credita o killer, atualiza placar/round, e
   **broadcasta** `0x4f` aos demais. No fim do stage o cliente reporta exp/gold (`0x53`) e o
   servidor **valida** (anti-cheat) e aplica.

⇒ O servidor é **árbitro + relay de estado**, não simulador de física. Consequência direta para o
bot: **um bot não tem cliente para reportar a própria morte nem para prever o hit** — então o
servidor tem de **sintetizar** os dois lados que faltam (a morte do bot e a detecção do hit
humano→bot). Ver §10.

Modos (`field+0x119`, `enum GameMode`): `1=Golem`, `2=Deathmatch`, `3=TeamDeath`, `4=Boss`
(+ `0` = modo "livre"/sem objetivo usado por alguns caminhos). Times: slots **0..9 = time 0**,
**10..0x13 = time 1** (`PlayerRec.Team`). Até 0x14 (20) registros por field.

---

## 1. Transporte e enquadramento

Dois canais coexistem dentro do stage:

| Canal | Transporte | Confiável | Roteamento | Conteúdo |
|---|---|---|---|---|
| **FIELD** (world) | TCP :40708 | sim | **servidor relaya** (`BroadcastField`) | opcodes 0x3b–0x6e: estado, ações, morte, placar |
| **GAMEPLAY** | UDP :40708/:40709 | não | P2P/relay (`CNetMessage`) | 0x30a move, 0x30f keystate, 0x311 golpe |

**Frame TCP** (`protocol-world.md`): `[u16 size][u16 A][u16 B][data]`, `size = 6+len(data)`.
- C→S: `A=opcode`, `B=seq` (valida `seq==último+1`, reset no login 0x0C; inválido → `Disconnect(2)`).
- S→C: `A=serverSeq` (contador por-user `user+0x1488`, ++ a cada envio), `B=msgType`.
- Cifra **AES-128-ECB** (T-tables, chave hardcoded `E1 3A 7E F5 …`, IV-seed 0xc47f) só no
  **outbound LOBBY**; o inbound validado é texto. (`PacketCrypto`.)

**Gate in-field** (em TODO handler de stage): `InField (user+0x1460) != 0` &&
`FieldSecondary (user+0x14a4) != 0` && `Status (user+0x1440) == 3`. Falha → `Disconnect(<code>)`.

`Field.BroadcastField(msgType, body[, except])` = a cada ocupado: `SendMessage` (header
`[serverSeq][msgType]`). `BroadcastFieldPlaying` = só aos `state==4`. `except` exclui o sender
(relay de ação). `BroadcastLobby` = canal cifrado lobby.

---

## 2. Máquina de estado da partida (`FUN_00409940` motor por-field)

Estado em `Field`: `State` (`field+8`: 0=livre, 1=fim-match, 2=em jogo), `Phase`
(`field+0x2b4`: `Pre=0`, `Playing=1`, `RoundEnd=2`), `DeadlineMs` (`field+0x2b8`), `Round`
(`field+0x2bc`), `MaxRounds` (`field+0x11a`), `RoundDurationSec` (`field+0x11c`, default 432),
`FragLimit` (`field+0x11e`), `Wins0/Wins1` (`field+0x2c0/0x2c1`), `Score0/Score1`
(`field+0x11f/0x120`), `WinnerSide` (`field+0x2bf`).

**Ciclo de vida (do lobby ao resultado):**

```
0x3b FieldCreate ─► (sala, State=0)
   │  0x41 config (host)  0x42 ready/lock  0x3d weapon  0x3e seat  3f/40 cleanup
   ▼
0x43 ENGAGE (host) ─► START: State=2, players 1/2→3, StartRound, broadcast 0x43[0], Phase=Pre, deadline+40s
   ▼
0x45 SPAWN (cada player, balanceia time) ─► state=3 → 0x48 ready → state=4 (Playing)
   ▼
[PLAYING]  movimento 0x30a/0x4b · golpe 0x311 · morte 0x4f · placar
   │  fim de round por: frag-limit (OnPlayerDeath) | tempo (deadline) | objetivo (Golem=0) | host-charge (0x4a/0x4d)
   ▼
0x4a ROUND END (Phase=RoundEnd, intermissão 15s) ─► próximo round (StartRound) até MaxRounds
   ▼
0x44 MATCH END (reason 2) ─► clientes voltam à sala
   ▼
0x53 GameResult (cliente reporta exp/gold) ─► servidor valida + aplica + 0x51/0x52/0x0a
```

Frames de status do motor (sintetizados em `Field`):
- **`0x48 FieldStatus`** (12B, ground-truth da captura que **destravou** o controle):
  `[48 00][round][u16 secsRestante][win0][win1][14 14][00 a0 0f]`. Mandar 9B truncado congela o
  input do cliente (val=05).
- **`0x49 NovoRound`** (5B): `[49 00][round][mvp0][mvp1]`.
- **`0x4a FimRound`** (corpo 4B): `[lastWinner][winnerSide][wins0][wins1]`.
- **`0x44 FimMatch`** (formato longo capturado): `[44 00][reason][00][u32 1][roomName]` — a versão
  curta 3B é **ignorada** (cliente não volta à sala).

---

## 3. Entrada / criação / spawn

### 3.1 `0x3b FieldCreate` (`FUN_00423580`) — cria a sala/field
Gate Status==2 (FieldLobby). Parse: `name(<0x29)\0 senha(<9)\0 desc(<0xc9)\0` + `map(<100)`
`mode(1..4)` `timeFlag(<0x16)` `u16 mapSlot(0x122..0x4ba)` `b3` `minLevel(b4)` `maxLevel(b5≤99)`.
Validações de modo (mode 2: `b3∈[0xd,0x1e]`; mode 3: faixa própria) + level-range do cash do
jogador. Acha fieldSlot livre, inicializa (`MasterSlot=seat`), ack LOBBY `0x3b [result][playerSlot]`.

### 3.2 `0x41 config-in-field` (`FUN_004077c0`, host) — reconfigura
Só host, fora de match (`State!=2`), `Mode!=0`. Grava name/sub/desc + `MapId` + `MaxRounds`
(`+0x11a`) + `RoundDurationSec` (`+0x11c`) + `FragLimit` (`+0x11e`) + `Mode` (`+0x119`).
Broadcast `0x41` com tudo concatenado.

### 3.3 `0x43 ENGAGE` (`FUN_00424210→FUN_004079d0`, **só host**, DISC 0x79 se não-host)
Ponto de "liberar combate". Avalia por modo se há condição de início (mode 2 exige ≥2 players;
modos 1/3/4 comparam contagens de time). Se ok / host-locked / ninguém mais pronto → **START**:
`ResetMatch` (zera rounds/wins/placar do match anterior — necessário p/ rematch), `State=2`,
players 1/2→3, `StartRound`, broadcast `0x43[0]` a todos, `Phase=Pre`, `deadline=now+40s`. Abort →
`0x43[cause]` só ao host.

### 3.4 `0x45 SPAWN` (`FUN_004242c0→FUN_00407c70`) — entra no campo + **balanceia time**
Requer `State==2`. Survival (mode 2): spawn direto. Demais: conta ocupados (state 3/4 vivos) nos
blocos 0..9 vs 10..0x13; recusa (`result=2`) se o time do seat já está mais cheio (salvo
host-lock `+0x127`). OK: `state=3`, zera kills/score, **broadcast `0x45 [seat]` a todos** +
housekeeping de MVP. (É este `0x45` que o bot sintético usa para **aparecer** no stage.)

### 3.5 `0x48 ready` + `0x4b AddPlayer` (avatar 3D)
`OnPlayerReady` (handler 0x48): `state→4` (Playing); em fase Pre, inicia o round quando ninguém
mais está `ready`. O **avatar 3D** é instanciado por `0x4b AddPlayer` no canal FIELD —
`CSessionState::AddRemotePlayer @engine 0x10e2b0`: `AddPlayer(slot)` + desserializa o **blob de
67B** (posição atual + anchor + ids + stats), lido pelo `vtable+0x118` do `CPlayerCharacter`.
Layout do blob (`BotMovement.BuildStageAddPlayer`, decode byte-a-byte):
`[u8 seat][u16 67][lead 08][f32 X][f32 Y][f32 Z][f32 0][u32 animId=31][u32 1][u8 0][f32 X2]
[f32 Y2][f32 facing][u32 90][u32 100][u32 110][u32 100][u32 15][9×00]`.

---

## 4. Movimento (DOIS canais)

### 4.1 GAMEPLAY UDP — `0x30a` move+ação (o que o avatar renderiza por frame)
SEND `CPlayerSource::SendAction(_Relay) @engine 0x103cb0`; RECV
`CSessionState::GetActionFromMessage @0x10afe0`. Datagrama **26B**:
`[u16 0x030a (|0x8000 se reliable)][u32 seq][u8 srcSlot][corpo 19B]`. Corpo (ordem de
`CNetMessage::Write`):

| off | campo | tipo | semântica |
|----|-------|------|-----------|
| 0 | `dt` | u16 | delta ms (~100; 0 = sem interpolação) |
| 2 | `actState\|slot` | u16 | `(actState<<5? )`; **5 bits baixos = srcSlot** (autor), altos = estado/flag move (0x20) |
| 4 | `x` | s16 | `pos.x / 0.01` = **pos×100** (`PackFloatToSWord`, SCALE `_DAT_3621acac=0.01`) |
| 6 | `y` | s16 | pos.y×100 |
| 8 | `z` | s16 | pos.z×100 |
| 10 | `heading` | s16 | graus assinados [-180..180] (short cru) |
| 12 | `subFrame` | u8 | nonce que **varia** todo frame (cliente exige mudança, não valida valor) |
| 13 | `ax/ay/az` | 3×s16 | **action-vec**: mira/direção do golpe ×100 (0 parado) |

`0x30f keystate` (14B) acompanha SEMPRE o `0x30a`: `[0x030f][u32 seq][u8 srcSlot][u8 srcSlotEcho]
[u16 0x0008][u16 0x0001][u16 tail]`, tail `0x0100`=andando / `0x0300`=parado (cliente lê o par
posição+keystate p/ escolher a animação).

### 4.2 FIELD TCP — `0x4b` move relay (reliável, server-relayed)
`0x4b` (`FUN_004247b0→FUN_00405c00`): C→S `[u16 len≤200][blob]`; requer `State==2 && sender
state==4`; **servidor broadcasta** `0x4b [senderSlot][u16 len][blob]` aos `state==4` **exceto o
sender** (`BroadcastFieldPlaying(except)`). `0x4c` (`FUN_00405cc0`): mesma coisa **direcionada a 1
alvo** (`[targetSlot][u16 len][blob]` → só ao target, msgType 0x4b).

> **Hipótese do `0x4b`-como-atalho — TESTADA E REFUTADA (captura 2026-06-27).** Instrumentei o
> handler `0x4b` e capturei um cliente andando num stage: **ZERO `0x4b`**. O movimento sai
> **exclusivamente** como UDP `0x30a` (+`0x30f`) pro socket de gameplay do servidor — golden
> decodificado (`slot=10 dt=100 act=0x002A pos=(1.19,0,-0.14) head=-7 aim=(-0.81,1.78,5.66)`),
> bate byte-a-byte com `BotMovement.EncodeActionBody`. **Não existe carrier TCP de movimento.** O
> `0x4b` FIELD relay é para outras ações in-field, não para a posição por-frame. ⇒ o único caminho
> de movimento é o `0x30a` UDP, **que é exatamente o que sofre o muro do peer** — logo o **caminho
> A (engine headless registra o bot como peer)** é o jeito, não há atalho de protocolo.

---

## 5. Combate (cliente-autoritativo)

### 5.1 Golpe — `0x311` (UDP) + action-vec do `0x30a`
`0x311` (10B): `[0x0311][u32 seq][u8 srcSlot][u8 sub][u16 actionId]` — emitido **no instante** do
golpe. `actionId` = id de arma/combo/skill (`1`=básico; `4/8/9`=combo, vistos na captura). O
**impacto/direção** viaja no `action-vec (ax/ay/az)` do `0x30a` seguinte. A detecção de hit
(raycast/alcance) roda **no cliente do atacante** (lógica SE1 `CCastRay` + regras Rakion de
arma/alcance — **geometria exata = lacuna §11**).

### 5.2 Troca de arma — `0x3d` (`FUN_00407520`)
`dir 0`: armaB→A (`WeaponState 2→1`); `dir 1`: A→B (`1→2`); broadcast `0x3d [slot][dir]`.

### 5.3 Morte — `0x4f Die` / `GameDiePlayer` (`FUN_00424a20→FUN_004087d0`)
**A vítima reporta a PRÓPRIA morte.** Parse `[killerSeat, cause]`. Servidor (`OnPlayerDeath`,
`FUN_00407e00`): marca `Dead`, `state=1` (aguardando respawn), credita o killer
(**score por causa**: `cause 8 → +2`, `cause 1 → +0`, senão `+1`; soma no `Score` do killer e no
`Score0/Score1` do time), `RecomputeMvp`, reatribui host se a vítima era o host. **Broadcast
`0x4f` (7B):** `[4f 00][victimSeat][cause][killerSeat][score0][score1]` aos `state==4`.
Frag-limit atingido → `EndRound` (gated por modo). (No caminho **solo** o `0x4f` é ecoado +
`0x4a` de resumo com `2bd=1` + `0x44` — `ClientSession.LobbyFlow.cs`.)

### 5.4 Saída/morte do próprio — `0x46` (`FUN_00424350`)
O cliente manda ao **SAIR do stage** / cair de combate. `flag<2` → refund (consome cash,
`FUN_0040b900`: `cost=(cashCost>>1)+param*5`). Sempre `OnPlayerDeath(self, killer=-1, cause=0)` +
**broadcast `0x46 [deadSlot]` a todos (incluindo o sender** — é o eco que conclui a saída; sem ele
o cliente trava). 0 vivos → `EndMatch` + `0x44` reason 2.

### 5.5 Ações de campo (relay) — `0x42/0x43/0x45/0x4a/0x5b/0x60/0x62/0x6e`
`Op_FieldPlayerAction` (0x5b, `FUN_00409080`, action<0x12), `Op_FieldUnitCommand/Stop/ByteAction/
CharAction` (comandos de unidade/golem, relayados como 0x42/0x43/0x45/0x4a), `Op_FieldTargetCommand`
(0x60, só líder), `Op_FieldRelayAction` (0x62, direcionado), `Op_FieldUseItem` (0x6e). São **relays
de ação** com guards de estado; o efeito de jogo roda nos clientes.

### 5.6 Sync — `0x6b RequestFieldTick` / `0x6c RequestFieldSnapshot`
`0x6b` (`FUN_004286a0`): responde subtype `0x1e [fieldHandle]`. `0x6c` (`FUN_00428750`): snapshot
do field em 3 blocos (A: 0x13+4B/entrada; B: 4+1B/entrada; C: cabeçalho estendido). Modelados como
snapshot vazio preservando o layout. (Polling de estado do stage pelo cliente.)

---

## 6. Round, fim de round e fim de match

`StartRound`: `Phase=Playing`, `Round=1+`, `deadline=now+(dur+3)s`, zera `Score0/1`, reseta Golems
(100%), `Dead=false`, `state 3→4`. **Gatilhos de fim de round:**
1. **Frag-limit** (`OnPlayerDeath`): Deathmatch = 1º player ao `FragLimit`; demais = placar de time.
2. **Tempo** (`deadline` no motor): `DecideRoundWinnerByScore` (DM = maior Score; times = Score0 vs
   Score1; empate = 2).
3. **Objetivo** (`DamageGolem`→0): `EndRoundObjective(timeAdversário)`.
4. **Host-charge** (`0x4a` dir 2/3, `FUN_00405a90`; `0x4d` facing x/y, `FUN_00405d70`): só host,
   credita win a um time e dispara `RoundEnd`.

`EndRound(winnerSide)`: `Wins0/1++`, `Phase=RoundEnd`, intermissão 15s. Após `MaxRounds` →
`EndMatch(reason)`: `State=1`, `Phase=Pre`, players 3/4→1. `0x44` devolve à sala.

**Respawn.** Não há opcode dedicado de respawn. O jogador morto fica `state=1` ("eliminado/
aguardando respawn"). Renasce de dois jeitos: (a) no **início do próximo round** (`StartRound`
zera `Dead` e faz `state 3→4`); (b) **mid-round** re-enviando `0x45` (mesma lógica de spawn/
balanceamento, `Combat_0x45_SpawnInto` — `state→3` + broadcast `0x45 [seat]`). O timing exato do
respawn automático mid-round (delay) é regra do cliente — **lacuna §11**.

---

## 7. Objetivos — Golem / Boss

Cada time tem um **Master Golem** com energia (`Golem0Hp/Golem1Hp`, 0..100 — string do stage-DB
"Master Golem has <%d%%> energy"). `DamageGolem(team, dmg)` reduz; ao zerar, o time **adversário**
vence o **round** (`EndRoundObjective`), e o match segue até `MaxRounds`. Fórmula de dano ao golem
e o broadcast de "energy %%" = **lacuna §11** (balanceamento).

---

## 8. Resultado da partida — `0x53 GameResultReport` (`FUN_00425010`)

No fim do stage **o cliente reporta** o resultado; o servidor **valida e aplica**. Parse:
cabeçalho `[b0<100][b1<6][count<5]` + `count×u16 seats` + `u32 exp` + `u32 gold` + `3×u32 drops`.
Fluxo:
1. Guards de estado do field (`StateB==2 && StateMode==2`, `FieldFlag119==0`).
2. **Bônus PU** (`pu_config`): `exp=BonusExp(exp)`, `gold=BonusGold(gold)`.
3. **Anti-cheat** (`FUN_0041cf80`): valida game-point; inválido → log "Wrong Game Point!" +
   `Disconnect(0xa0)`.
4. Aplica exp/gold + **level-up** (`FUN_0040d300`): se subiu, LOBBY `0x51 [newLevel][extra]`.
5. **Drops/quest** (`FUN_0040b940`): se atualizou, broadcast `0x52 [seat][newLevel][q0][q1]`.
6. **Snapshot final** `SendMessage(0x0a, …)` (estado pós-partida: gold, level, stats, seats, …).

> **Domínio ≠ rede:** a progressão (exp→level, gold, rank) é regra de domínio
> (`WorldServer.ApplyStageResult`/`GrantExp`), não do handler; o `0x53` só traduz bytes↔chamada.

---

## 9. Catálogo de opcodes do stage PvP

| Op | FUN | Nome | Dir | Corpo / efeito |
|----|-----|------|-----|----------------|
| 0x3b | 00423580 | FieldCreate | C→S | cria sala; ack LOBBY 0x3b |
| 0x3d | 00407520 | WeaponSwap | C→S | `[dir]`; bc 0x3d `[slot][dir]` |
| 0x3e | 004075a0 | ReSeat | C→S | move bloco fila↔jogo; bc 0x3e `[old][new]` |
| 0x3f | 00405740 | StartVote/Disband | C→S | zera records, `State=0` |
| 0x40 | 004097c0 | Destroy/Kick | C→S | host derruba slot |
| 0x41 | 004077c0 | Config (host) | C→S | grava map/mode/rounds/dur/frag; bc 0x41 |
| 0x42 | 00407910 | Ready/EquipLock | C→S | trava/destrava slot; bc 0x42 `[val][arg]` |
| 0x43 | 004079d0 | **Engage/Start** | C→S | host inicia; bc 0x43 `[0]` |
| 0x45 | 00407c70 | **Spawn** | C→S | balanceia time; bc 0x45 `[seat]` |
| 0x46 | 00424350 | Self-Death/Exit | C→S | bc 0x46 `[seat]`; 0 vivos → 0x44 |
| 0x48 | — | FieldStatus | S→C | 12B status (destrava input) |
| 0x49 | — | NewRound | S→C | `[round][mvp0][mvp1]` |
| 0x4a | 00405a90 | RoundEnd/Charge | C→S/S→C | `[lastWin][side][w0][w1]` |
| 0x4b | 00405c00 | **MoveRelay** | C→S→C* | `[slot][u16 len][blob]` (exclui sender) |
| 0x4c | 00405cc0 | DirectedAction | C→S→C | só ao target (msgType 0x4b) |
| 0x4d | 00405d70 | Facing/RoundEnd | C→S | x/y → win de time |
| 0x4f | 00424a20 | **Die** | C→S→C | vítima reporta; bc `[victim][cause][killer][s0][s1]` |
| 0x44 | 00407be0 | MatchEnd | S→C | reason 2; volta à sala |
| 0x53 | 00425010 | **GameResult** | C→S | exp/gold/rank; valida+aplica |
| 0x5b | 00409080 | PlayerAction | C→S→C | `[slot][action<0x12]` |
| 0x60 | 00405ef0 | TargetCommand | C→S | só líder |
| 0x62 | 00406930 | RelayAction | C→S→C | direcionado |
| 0x6b | 004286a0 | FieldTick | C→S | resp 0x1e `[handle]` |
| 0x6c | 00428750 | FieldSnapshot | C→S | resp 0x1f (3 blocos) |
| 0x6e | 0040e5f0 | UseItem | C→S→C | `[type][s16 arg]` |
| **0x30a** | eng 103cb0 | Move+Action | UDP | 26B (pos×100, heading, action-vec) |
| **0x30f** | — | Keystate | UDP | 14B (animação) |
| **0x311** | — | Attack | UDP | 10B (`actionId`) |

---

## 10. Arbitragem server-side do bot (o que já existe vs o que falta)

**Pronto (`Field.cs` + `WorldServer.BotAi.cs`):** spawn (0x45/0x4b), IA (alvo mais próximo →
perseguir/patrulhar → mirar/atacar com cooldown), síntese de movimento (`EmitBotMovement` =
0x30a+0x30f), golpe (`EmitBotAttack` = 0x311), **morte do bot sintetizada**
(`BotTakeDamage`→`OnPlayerDeath`+broadcast 0x4f victim=bot), placar/MVP/round nativos, limpeza.

**Os 3 lados que o servidor PRECISA sintetizar (porque o bot não tem cliente):**

1. **Renderizar o movimento do bot no host** — **RESOLVIDO server-side via handshake 0x319** (§10.3).
   O muro do peer NÃO exigia engine headless: era um endpoint UDP por-player não-registrado. O atalho
   do `0x4b` segue refutado (§4.2: movimento é só `0x30a` UDP).
2. **Detecção de hit humano→bot** — **IMPLEMENTADO** (`Field.ResolveBotHitByHuman` + `BotPlayer.TakeDamage`,
   chamado no 0x311 do humano em `UdpGameplay`). O bot não reporta a própria morte (cliente-autoritativo), então
   o servidor arbitra: no 0x311 do humano, acha o bot inimigo mais próximo dentro de `HumanMeleeRange` (6.5),
   aplica dano (throttle anti-instakill) e, na morte, sintetiza o **0x4f** `[botSeat,cause,killerSeat,score]`.
   APROXIMAÇÃO: geometria = distância XZ (cone/raycast por arma = §11); dano = placeholder 40 (`MeleeDamageFor`,
   até a RE de `actionId`→arma→dano, §11).
3. **O golpe do bot no humano** — funciona pelo modelo nativo: o bot emite 0x311/action-vec, **o
   cliente do humano prevê o hit e reporta a própria morte (0x4f killer=botSeat)** — *desde que o
   golpe do bot chegue ao humano* (mesmo muro do item 1).

---

## 10.3 MURO DO PEER RESOLVIDO — handshake de endpoint 0x319 (server-side, sem engine headless)
RE estática completa do caminho de recv do gameplay UDP no `engine.dll` (2026-06-28) cravou que o muro do
movimento **não** era registro off-wire na engine — era um **filtro de endpoint por-player** que o servidor
pode satisfazer.

**O gate do movimento:** `CSessionState::IsValidUDP_ForPlayer @0x36109da0` (chamado por `IsApplyReliableUDP
@0x36109e20` ANTES de `GetActionFromMessage @0x3610afe0`) compara o `(IP,porta)` de ORIGEM do datagrama 0x30a
contra `playerRec[slot]+0x1e8 (addr)` / `+0x1ec (port)`. Se não bate → ação descartada.

**Quem grava esse par:** SÓ o handshake **opcode 0x319** (dispatcher @`0x361005ad`):
```
361005b4: mov cl,[esp+0x3f]      ; slot = datagrama offset 7
361005b8: cmp cl,0x14            ; slot < 20
361005c9: call [vtable+0x8]      ; player-array (stride 0x378)
361005e3: add eax, slot*0x378
361005e5: mov [rec+0x1e8], <fromAddr>   ; grava IP de ORIGEM do recvfrom — INCONDICIONAL
361005f0: mov [rec+0x1ec], <fromPort>   ; grava porta — sem host-check/sequência/adr_uwID
```
O `type-7` (AddRemotePlayer @0x3610e2b0) preenche `playerTable[slot]` (+0x1d20) — por isso o bot APARECE — mas
**não** toca `+0x1e8/+0x1ec`. Sem o 0x319, o 0x30a do bot morre no gate por endpoint zerado/divergente.

**A brecha (implementada):** o servidor manda um **0x319 "do slot do bot" do SEU socket UDP** (o mesmo `_sock`
que relaya os 0x30a). O cliente grava `playerRec[botSlot].addr/port = servidor:40708`; os 0x30a relayados (também
de `servidor:40708`) passam no `IsValidUDP_ForPlayer`. **Sem engine headless, sem 2º cliente.**

**Wire do 0x319 (cravado, 8B):** `[u16 0x0319][u32 seq][u8 slot@6][u8 slot@7]` — o handler lê só opcode (offset 0,
caminho unreliable: bit 0x8000 limpo, `test ch,ch; jns @0x361002ae`) e slot (offset 7). Datagrama copiado p/
`[esp+0x38]` = offset 0 (confirmado pelo caso `0x4000`=input do cliente). Implementação: `BotMovement.BuildPeerRegister`
+ `UdpGameplay.EnsureBotEndpointRegistered` (envia antes do relay do 0x30a, throttle 1.5s por (humano,slot)).
Golden test `PeerRegister_0x319_LocksWireLayout`.

**RESSALVA (capture-backed, peer_registration_re.out.txt): o 0x319 sozinho é provavelmente INCOMPLETO.** A captura
real do stage (`stage_udp_capture.txt`) mostra que o gate de APLICAÇÃO do 0x30a de um peer no host só abre após um
**connect-de-sessão lockstep**: pares `0x0304`(janela-de-token)↔`0x0305`(eco) na :40709, intercalados com `0x0319`
(ack), ANTES do primeiro 0x30a daquele peer (l.55-327 da captura; 0x0304/0x0305 são byte-idênticos exceto opcode
04↔05, mesmo token = offset do stream reliable). O `IsApplyReliableUDP @0x36109e20` checa `IsValidUDP_ForPlayer`
(endpoint, que o 0x319 grava) **E** `IsRightSequence` (sequência, que o lockstep estabelece). O bot faz transporte
(0x0201) + spawn (0x4b) mas NÃO o lockstep → o host fica fora de "networked com o bot". **Implementado (PASSO 1a):**
`UdpGameplay` ECOA `0x0305` no lugar do bot p/ cada `0x0304` do host (bytes idênticos, opcode 04→05) + LOG diagnóstico
de todo 0x0304/0x0305/0x0319. O teste in-game agora REVELA se o host emite 0x0304 ao bot (se sim e o eco bastar →
anda; se não, o gatilho do connect é outro — provável frame TCP de "player entrou no field", ver
peer_registration_re §7). **Risco real:** se exigir o stream reliable COMPLETO (TAGV/ConnectRemoteSessionState), é a
MESMA parede do headless (§12 headless-engine-re) — o corpo do 0x0304 é o connect off-wire.

Se validar, o `BotPeer`/`SessionHandshake`/`BotEngineHost` (modelo "bot conecta no humano como host" — invertido,
o humano é `ga_IsServer=0`) viram **código morto a remover**.

## 10.4 NOME/LEVEL do bot no stage — vem do type-7 (identidade de sessão), não do 0x4b/roster (RE 2026-06-28)
O bot aparece com **Lv0/sem-nome** no stage porque o spawn atual (`0x4b` FIELD, `BuildStageAddPlayer`) só instancia
o avatar — NÃO popula a identidade de sessão. RE do `AddRemotePlayer @0x3610e2b0` (o handler type-7) cravou que a
identidade vem por ELE:
```
3610e2f1: call 0x3610b6d0          ; Activate -> playerTable[slot]+0x1d20 (avatar)
3610e30e: mov ecx,[esp+0xa04]      ; ptr do NOME; len em [esp+0xa00]
3610e31e: call 0x36100d50          ; constrói CTString(nome)
3610e33b: call [vtable+0x118]      ; player.SetName(nome)
3610e37e: call [vtable+0x114]      ; 2a string (tag/team)
3610e395: call 0x36100cf0          ; lê BLOB de 0x200 bytes (CPlayerCharacter serializado: level/classe/aparência)
3610e3a9: call [vtable+0x11c]      ; player.SetCharacter(blob 0x200)
```
⇒ pra dar nome/level ao bot no stage, o servidor tem de mandar um **type-7** com [slot][nome CTString][tag][blob
0x200 do CPlayerCharacter]. **GAP:** (a) o **envelope wire** do type-7 vem do dispatcher que chama `0x3610e2b0`
(outra camada de RE), e (b) o **blob 0x200** é o `CPlayerCharacter` serializado (o mesmo formato cujo appearance/
char-data já travou o headless, §12 de headless-engine-re.md). Decodificar byte-a-byte exige **captura 2-jogadores**
(um humano entrando no stage de outro) OU RE do dispatcher + da serialização do CPlayerCharacter. É **sub-projeto**,
não quick-fix — e construir por palpite viola a lição-mestra (frame que o cliente não viu → crash).

---

## 11. Lacunas de RE (para fechar o "completo")

| Lacuna | Onde está | Para quê |
|---|---|---|
| **Geometria de hit** (raycast/alcance/cone por arma) | `rakion.exe` (lógica de combate) + SE1 `CCastRay` | detecção humano→bot fiel (§10.2) |
| **Valores de dano** (arma/skill/combo, HP, armor) | data (`iteminfo`/`classlevelinfo`) + `rakion.exe` | `BotTakeDamage` aplicar dano certo |
| **Catálogo de `actionId`** (0x311: 1/4/8/9…) | captura 2-players + `rakion.exe` | mapear golpe→arma/combo |
| **Fórmula de dano ao Golem** + broadcast "energy %%" | `rakion.exe` / stage-DB | objetivo Golem/Boss fiel |
| **Respawn** (timing, frame) | `rakion.exe` / captura | bot e humano renascerem no round |
| **`0x4b` como carrier de movimento** | teste in-game (relay FIELD) | possível atalho do muro do peer |
| **Muro do peer** (render do movimento server-side) | engine.dll (netcode SE1) | caminho **A** (headless) / **B** (ponte) |

> Próximo passo natural (caminho **A** escolhido): provar que o `0x4b` FIELD relay move o avatar do
> bot na tela do host (atalho), e em paralelo a engine headless para registrar o bot como peer
> legítimo. Com a geometria de hit + valores de dano, o servidor fecha o combate humano↔bot 100%.

---

## 12. O MURO do HIT×N nativo cravado byte-a-byte: `player[slot]+0x1d8` (canal RELIABLE)

> **Data 2026-07-01.** RE da `engine.dll` (rakion-final, base 0x36000000, estável entre builds) +
> fonte SE1 (`Sources/Engine/Network`). Fecha *por que* o número nativo do combo (HIT×N) não aparece
> ao acertar o bot, e reduz o problema a **um único flag**.

### 12.1 O modelo (fonte SE1)
Cada cliente roda um **CServer loopback-local** (`Broadcast_Update_t`/`Server.cpp`). O transporte é
o `CCommunicationInterface` (SE1 puro): canais UDP com header `[UBYTE flags][ULONG seq][UWORD id]
[ULONG transferSize]` (`Packet.h`). Flags: `UNRELIABLE=0, RELIABLE=1, ACK=8, CONNECT_REQUEST=16,
CONNECT_RESPONSE=32`. Reliable só é **aceito de endereços já registrados** via CONNECT
(`Broadcast_Update_t` aloca `cm_aciClients[slot].ci_bUsed=TRUE`).

### 12.2 O gate, cravado no disasm
O array de players do jogo (`[0x3636F260]→[obj]→call [vt+8]`, **stride 0x378** por slot) tem:
- `+0x1e8` = endpoint IP, `+0x1ec` = porta — o **gate NÃO-confiável** (`IsValidUDP_ForPlayer
  @0x36109da0`), escrito pelo **0x319** que já forjamos → é o que deixa o `0x30a` (movimento) passar.
- `+0x1d8` = **flag "canal reliable ativo" do slot**. É o **gate CONFIÁVEL**, checado em **todo**
  branch de mensagem reliable/connect do dispatcher `@0x361001f0`:
  - `@0x360ffee8  cmp dword [player+slot*0x378+0x1d8], 1 ; jne (dropa)`  — tipos < 0x14
  - `@0x361002f1  mov edx,[player+slot*0x378+0x1d8]; test edx,edx; je (dropa)`
  - `@0x361004b9  (idem, DENTRO do branch `cmp cx,0x304`)` — o próprio 0x304 é **pós-conexão**, gated.

  O índice é o **slot do REMETENTE** declarado na mensagem (`movzx ecx,[esp+0xf]; imul ecx,0x378`).

### 12.3 Por que trava o HIT×N
O `0x8307`/create-com-colisão do bot é **mensagem reliable**. Como `player[botSlot]+0x1d8 == 0`
(o bot nunca completou CONNECT), **o dispatcher DROPA o create** → o bot nunca vira `CEntity` real
com colisão no cliente do humano → o raycast de hit do humano não acha corpo → `AddHitCount`
(contador do combo, LOCAL no atacante) **nunca é chamado**. O `0x30a` passa porque usa o gate
**+0x1e8** (não-confiável), independente do `+0x1d8`.

### 12.4 O writer de `+0x1d8` NÃO é forjável por 1 mensagem (≠ 0x319)
Varredura completa da `.text`: **nenhum** store literal/simples escreve `player[..]+0x1d8` (nem
`mov [reg+0x1d8],1`, nem SIB `[base+idx+0x1d8]`). O único `mov [reg+0x1d8],dword` é um **construtor
de bounding-box** (`0x7f61b1e6`≈FLT_MAX @0x361a7273), não-relacionado. Logo `+0x1d8` só é setado
pelo **handshake CONNECT completo** (objetos-conexão de `0x414` bytes com `+0x408=1`, criados no
branch `cmp cx,0x4000` @0x361003dd, keyed por addr `+0x400`/porta `+0x404`) seguido do registro do
player. É **exatamente** o que o mini-peer (`RakionServer.Peer`) tenta e onde **estola**: contra um
cliente-só, que não hospeda a conexão do bot, o CONNECT não fecha (o `0x0304` volta como `0x0305`
ack, não completa). **Chicken-and-egg**: o 0x0304 que avançaria a conexão é ELE PRÓPRIO gated por
`+0x1d8`.

### 12.5 Consequência — o número virou UM dword
O caminho reliable-server-side está **arquiteturalmente barrado** sem: (a) 2º cliente real (VETADO,
multiplayer), ou (b) headless peer (H3, barrado pela `gamemp.dll` packed), ou (c) **1 write
in-process**: setar `player[botSlot]+0x1d8 = 1` na memória viva do cliente do humano. Com esse flag,
o `0x8307` que o servidor **já emite** passa pelo caminho **nativo** do jogo → entidade real com
colisão → HIT×N dispara sozinho. É cirúrgico e **sem clipping** (não move nada; só destrava o create
que o servidor sintetiza). É o análogo reliable do 0x319, mas como o writer não é 1 mensagem, entra
por injeção mínima em vez de pacote. Ver [[bot-hittability-type7-verdict]] e [[headless-engine-host]].
</content>
</invoke>
