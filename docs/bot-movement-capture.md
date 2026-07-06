# Bot — protocolo de gameplay (RE) e o que falta p/ movimento/combate

O sistema de bots está implementado de raiz **menos** um ponto: emitir o movimento/ataque do bot no
formato que o cliente renderiza. A RE abaixo (engine.dll, Ghidra) já decodificou o essencial — falta
**uma constante**, confirmar o **carrier** e **validar in-game** (iterativo, com o cliente rodando).

Tudo o mais (domínio, `/addbot`, spawn, IA, simulação de morte, limpeza, re-add) está pronto e
testado. O ponto gated vive isolado em
[`BotMovement`](../server/RakionServer/src/RakionServer.World/Network/BotMovement.cs).

> Regra-mestra: decodificar (RE/captura) e **SINTETIZAR** da posição do bot — nunca relay do pacote.

## Onde mora a lógica (confirmado)

`engine.dll` (ImageBase 0x36000000) implementa `CSessionState` e `IScavengerWorldNet`; `gamemp.dll`
tem os overrides + a instância global `_pRakionWorldNet`. O cliente (`rakion.bin`) importa tudo. Há
**dois** esquemas de numeração: os **opcodes do world** (pequenos: 0x43 engage, 0x45 spawn, 0x4b move
relay, 0x4f die) e os **CNetMessage de gameplay** (0x30X) que viajam DENTRO do payload de gameplay.

## Spawn de um player remoto (roster) — `CSessionState::AddRemotePlayer`

`?AddRemotePlayer@CSessionState@@QAEXEGPAD@Z` @ engine.dll 0x10e2b0:
`AddRemotePlayer(uchar slot, ushort blobLen, char* blob)` → `AddPlayer(slot)` + desserializa `blob`
(info do player: nome/stats) no `CEntity` do slot (vtable+0x118) + `SendInfoCreateNpcTo(...)`.

⇒ Para o bot APARECER num slot, o cliente precisa receber a msg que chama isto, com o **blob de info
do player** (o `blob`/`blobLen`). `AddPlayer` cria um `CPlayerCharacter`; o `blob` é lido pelo
**vtable+0x118** dessa entidade (RE pendente: rastrear a vtable de CPlayerCharacter). Forte candidato
ao formato do registro: o per-player do worldserv `FUN_0040afb0` = `[name\0][class@1531][team@146c]
[dword@14d0]` (já decodificado), usado nos 0x1e/0x1f.

**Task #3 — multi-membro NUNCA exercitado.** São DUAS aparições: (1) o slot da SALA pré-match (UI de
lobby, alimentada pela lista de canal 0x1e + delta de roster 0x2d `[id:4][slot:1]` por membro mudado,
worldserv `FUN_0040c960`/`FUN_0040bcb0`) e (2) o avatar IN-FIELD (`AddRemotePlayer`). A ENTREGA (quais
frames, quando, o id de membro) nunca rodou no servidor → precisa de iteração in-game. **Uma captura de
sessão real com 2 players mostra esses frames byte-a-byte** e fecha a entrega + o blob de uma vez — o
mesmo método que resolveu 0x0C/lobby/inventário.

## Movimento+ação do PLAYER — CNetMessage **0x30a** (decodificado, é o que o bot usa)

O que um player envia a cada frame é o **0x30a** (move+ação juntos), não o 0x30b. SEND:
`CPlayerSource::SendAction_Relay` @engine.dll 0x103cb0; RECV/parse: `CSessionState::GetActionFromMessage`
@0x10afe0. Corpo (19B), na ordem de `CNetMessage::Write` (= a ordem de Read, casam byte-a-byte):

| off | campo      | tipo | semântica |
|-----|------------|------|-----------|
| 0   | `dt`       | u16  | delta de tempo (timeGetTime − last, cap 0xffff); 0 = sem interpolação |
| 2   | `act\|slot`| u8   | `(actState&lt;&lt;5) \| senderSlot` — 5 bits baixos = autor (relay), 3 altos = estado |
| 3   | —          | u8   | 0 (reservado) |
| 4   | `x`        | s16  | `PackFloatToSWord(pos.x)` = `pos.x / 0.01` = pos.x*100 |
| 6   | `y`        | s16  | pos.y*100 |
| 8   | `z`        | s16  | pos.z*100 |
| 10  | `heading`  | s16  | rotação (short cru, **sem** escala) |
| 12  | `flag`     | u8   | estado (jump = entity+0x10 & 8) |
| 13  | `ax`       | s16  | action-vec x (mira/direção do golpe) *100 |
| 15  | `ay`       | s16  | action-vec y *100 |
| 17  | `az`       | s16  | action-vec z *100 |

Implementado em `BotMovement.EncodeActionBody` (golden test `BotMovementTests`). O ataque é o MESMO
0x30a com `actState`/action-vec setados → o cliente do alvo processa o golpe e reporta a própria morte
(0x4f killer=botSeat), nativo. (O 0x30b é a variante position-only de NPC/golem; `kind` 2/3/4 + 4 floats.)

### Pack/unpack de posição (cravado)
- `UnpackFloatToSWord(s, &f)`: `f = (float)s * _DAT_3621acac;`  (engine.dll 0xf96d0)
- `PackFloatToSWord(f) -> short` (engine.dll 0xf96b0) — é o **encoder** do bot: `short = f / SCALE`.
- **SCALE = `_DAT_3621acac` = 0.01** (lido por RE) ⇒ `short = coordMundo * 100` (precisão 1/100,
  alcance ±327.67). `animParam` usa `_DAT_36218164 = 0.001`. Já fixados em `BotMovement.MoveScale`.

`CPlacement3D` = 6 floats (pos x/y/z + rot h/p/b); no wire só vão 3 posições packed + 1 heading.

## O que falta (preciso e pequeno)

1. ~~Ler SCALE~~ ✓ = 0.01. ~~Corpo do move~~ ✓ 0x30a (19B, `BotMovement.EncodeActionBody`).
2. ~~Carrier~~ ✓ UDP. ~~Wrapper do datagrama~~ ✓ DECODIFICADO (`FUN_36100ef0` @0x100ef0) e IMPLEMENTADO:
   datagrama = `[u16 msgType (0x30a; |0x8000 se reliable)][u32 seq][u8 srcSlot][CNetMessage body 19B]` =
   **26B** (o alvo é só o endereço do `SendTo`, não vai no pacote). Em `BotMovement.TryBuildActionDatagram`
   + `BotAi.EmitBotMovement` (UDP por-peer via `SendGameplayRaw`). **GATED em `UdpFramingKnown=false`**:
   só LIGAR após 1 captura golden-confirmar o datagrama byte-a-byte E que o cliente ACEITA um pacote de
   gameplay vindo do servidor (validação de origem/seq) — mandar forma não-vista pode crashar o cliente.
3. **Blob de info do player** (AddRemotePlayer/roster) — decompilar o leitor vtable+0x118 do CEntity.
4. **Plugar** em `BotMovement` (já tem o layout em comentário): `MoveFormatKnown=true`,
   `TryEncodeMove` monta `[0x30b][animParam][2][team][slot][packX][packY][packZ][heading]`.
5. **Detecção de hit** do humano no bot: parsear a ação de ataque (case 0x30a `GetActionFromMessage`)
   que o servidor já vê no relay, e chamar `WorldServer.BotTakeDamage`.
6. **Validar in-game** (iterativo): bot anda/persegue/ataca/morre/mata sem travar o cliente. Golden
   test byte-a-byte do blob sintetizado contra uma captura (mitm `openrakion-server:latest`).

## Captura (fecha movimento + roster de uma vez) — PASSO A PASSO

Harness pronto: [`tools/mitm_botcap.py`](../tools/mitm_botcap.py) (passthrough + LOG de TCP e UDP) e
[`tools/decode_bot_action.py`](../tools/decode_bot_action.py) (descobre o envelope real do datagrama +
decodifica o corpo 0x30a). Container `openrakion-server:latest` (já presente).

```
# 1) world ORIGINAL (Docker) — TCP+UDP de gameplay
docker run --rm -p 40708:40708/tcp -p 40708:40708/udp -p 40709:40709/udp openrakion-server:latest

# 2) o proxy de captura (loga em C:\temp\botcap.log)
python tools/mitm_botcap.py

# 3) cliente -> 127.0.0.1:41708 ; entre numa sala/stage e ANDE em +X, depois +Y, depois GIRE,
#    depois ATAQUE — movimentos isolados e repetidos. (1 cliente já emite o 0x30a; 2 = roster.)

# 4) decodifica
python tools/decode_bot_action.py
```

O decoder mostra as **famílias** de pacote UDP (marker/tamanho) e o **corpo 0x30a** decodificado
(slot/pos/heading/aim). Andar deve mover `pos.(x/y)` monotonicamente; girar muda `head`. O campo
`env=` é o ENVELOPE a cravar (seq+srcSlot+marker) — com 1 captura eu confirmo byte-a-byte, ligo
`BotMovement.UdpFramingKnown=true`, e o bot anda/ataca. Mesmo método de 0x0C/lobby/inventário.
