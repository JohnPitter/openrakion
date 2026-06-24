# Bot no STAGE 3D — como retomar (captura + combate)

Handoff de 2026-06-24. O sistema de bot está **funcional e validado in-game na SALA**; falta o bot
no **stage 3D** (aparecer + lutar). Este doc é o ponto de retomada.

## Estado (validado in-game pelo usuário)
- ✅ **Bot entra na sala** num slot RED/BLUE com **nome + level** (member-join **0x38**, RE byte-exata do
  worldserv `FUN_00406f40`@0x40735a + registro `FUN_0040b7f0`@0x40b7f0). Impl: `WorldServer.BotRoster.cs`.
- ✅ **`/addbot` / `/removebot` / re-add** (chat 0x47, não 0x56=Stub). Trigger: `TryHandleBotChatCommand`.
- ✅ **Limpeza**: bots somem ao sair/fim (`DiscardBots` manda 0x3a member-leave antes de `RemoveAllBots`).
- ✅ Field da sala pré-partida (`GetOrCreateRoomField` + `AssignSeat` + `Settled=true` — senão o tick
  `State==1 && !Settled` liquidava e sumia os bots).

## PENDENTE — bot no stage 3D (o "jogar contra ele")
O avatar 3D **NÃO** nasce de UDP nem dos frames TCP que tentamos (0x45/0x54/0x4c todos disparam server-side
mas o cliente não desenha). RE da **engine.dll** (agente): o spawn 3D é `CSessionState::AddRemotePlayer`
@0x10e2b0 (← `SendFieldGameAddPlayer/Reply`, vtable IScavengerWorldNet @0x36216828), **opcode reliable do
world 0x4b/0x4c** com o **MESMO blob** do 0x38 (`BuildBotPlayerRecord`, FUN_0040b7f0). Layouts candidatos:
- `0x4b`: `[u16 0x4b][u16 blobLen][blob]` (slot no blob, +0x1478)
- `0x4c`: `[u16 0x4c][u8 slot][u16 blobLen][blob]` (slot explícito) — foi o que tentei (`NotifyBotAddPlayer`).

Já implementado e **ligado** (`ClientFramesEnabled=true`): `SpawnFieldBotsInStage` (chamado do
`StartGameClock`/0x4b) emite 0x45 + 0x4c + 0x54 ao host no load. **Não funciona** — o frame/seq EXATO só a
captura confirma (o agente avisou). Artefatos de RE: `rakion-work/ghidra-proj/{joinflow*,stageenter,starttrig,
engine_blob,engine_spawn}.txt`.

Depois do spawn vêm: **movimento 0x30a** (UDP, `BotMovement.EncodeActionBody` cravado, gated
`UdpFramingKnown=false` — risco de validação de origem) e **hit-detection** (parsear o 0x30a do humano →
`BotTakeDamage`).

## Como CAPTURAR o frame de spawn (próxima sessão)
Ferramenta pronta: **`tools/mitm_botcap.py`** = proxy MITM que **decifra** o TCP (AES-128-ECB, chave no
script) e loga em `C:\temp\botcap.log` (`W->C ... u16a=0xNNNN ... data=hex`). Portas via env `RKMITM_TCP_IN/OUT`.

**O SNAG (resolver antes):** o cliente é amarrado ao MEU stack. `RakionLauncher` lança `rakion.exe` com
`serverId "1A"` (`MainForm.cs:13`); o cliente resolve o world a partir do serverId; o **login do launcher
(:80) checa o world online via o broker (40706)**. Parar meu world → launcher mostra "offline" (não lança).

**Setup de captura que funciona** (a fazer):
1. Manter MEU world server online (pro launcher passar o login) — broker 40706 + world.
2. Apontar o **serverId "1A" → o proxy** (que vai pro original). Achar onde o serverId vira IP:porta (lista de
   servidores do cliente; provável `/fetch` :80 ou config no Bin) e redirecionar pro proxy. OU rodar a stack
   ORIGINAL inteira (broker+world) nas portas que o serverId "1A" usa, e o proxy no meio do world.
3. Original: `docker run --rm -d --name rkorig -p 40808:40708/tcp -p 40808:40708/udp -p 40809:40709/udp openrakion-server:latest`
   e `RKMITM_TCP_OUT=40808 py tools/mitm_botcap.py` (proxy 40708→40808).
4. **Verificar a conta** no original (o Docker tem contas próprias; `test/test` pode não existir — checar o DB
   do container ou seedar).
5. **2 clientes** (patch do mutex). A cria sala PvP + Start; B entra + Start. ⚠️ a trava por-IP do original
   pode barrar 2 da mesma máquina no stage — se barrar, usar 2ª máquina. O frame de spawn é TCP (dispara no
   join do B), então tem chance de pegar antes do conflito UDP.
6. No log: achar o `W->C` com `u16a=0x004b`/`0x004c`/`0x0037` no instante que B entra → é o frame + blob exatos.

## Depois da captura
- Ajustar `WorldServer.BotRoster.cs` (`NotifyBotAddPlayer`) pro opcode/layout/seq exatos.
- Validar o datagrama UDP 0x30a pelo mesmo log (W->C/C->W UDP) → ligar `UdpFramingKnown` → bot anda.
- Hit-detection: parsear a ação 0x30a do humano (que o servidor vê no UDP) → `BotTakeDamage`.

## Botão NATIVO — RE COMPLETA (agente 2026-06-24, tela cravada)
A tela do GAME ROOM (RED/BLUE) = **`FUN_00446ff0`** @0x446ff0 (mode 0x1c; cria os botões + o grid de 20 slots
`FUN_0042fb10` @+0x9f4 — prova que é a sala, não lobby). Obj: alloc `FUN_004bf8c2(0xa28)`, ctor `FUN_004464a0`,
vtable `PTR_LAB_004d3770`, vivo em `DAT_004feed0+0x17c` (+0x180 = modo UI: 0x1c sala / 0x1d field). A antiga
`FUN_0044f080` era LOBBY; textos de botão são localização externa (sem âncora de string) e frida congela a UI —
por isso a RE foi por rastreio estático. Overlay `client/RakionLauncher/AddBotOverlay.cs` foi REJEITADO.
- **Etapa 1 (botão APARECE) — patcheado** (`tools/patch_botbtn.py`; aplica/restaura via `swap_botbtn.ps1`):
  hook `0x447329` (`MOV ECX,[ESP+0xac]`, 7B) → cave `0x515207` → `FUN_00437680(buf, ESI=screen, id=0x200,
  -1,-1,0,0x400,0)` + SetBitmap(screen+0x184/+0x1b4)/SetText/SetPos/SetSize(0x66×0x1d). PENDENTE: confirmar
  in-game + ajustar posição (BTN_X/Y).
- **Etapa 2 (clique → /addbot) — a fazer**: o csButton solta evento `{type=0x0d, *(ev+0xc)=cmd}` no HandleEvent
  da tela = **`FUN_00447af0`** (vtable+0x40; já trata 0x132 Start, 0x135 Previous, 0x137 team). Adicionar
  `case 0x200:` → `CNet::SendChatDataInGame((CNet*)(*_pNetwork_exref+0x119c), "/addbot", 0)` (mesma chamada de
  /kick,/notice em `FUN_0040df40` @0x40e1e2; o servidor já parseia /addbot). Artefatos:
  `rakion-work/ghidra-proj/ROOM_SCREEN_FINDINGS.out.txt` + `room_*.out.txt`. Ver [[re-room-ui-buttons]].

## ACHADO (ponto exato do redirect pra captura)
O endereço do world que o cliente conecta **NÃO** está num config solto — vem do **BROKER**:
`RakionServer.Broker/Systems.cs:200` `ServerListPacket(cliVersion)` monta a lista de `Systems.GSList` (cada
world REGISTRADO: `value.ip`/`value.wan` + `value.port`; layout `[IP 4][port BE 2][usedSala][maxSalas]
[usedSlots][maxSlots]`). O world se registra no broker via `RakionServer.World/Network/BrokerLink.cs` (lê
ip/porta do `worldserver.ini`). O launcher (:80) decide "online" pelo broker.

**Receita de captura (sem o usuário mudar nada no cliente):** manter o broker + um world registrado (status
online → o launcher passa o login) **e** fazer o `value.ip`/`value.port` desse registro apontar pro **PROXY**
(`mitm_botcap.py` na porta que o broker anuncia; o original atrás dele via `RKMITM_TCP_OUT`). Assim o cliente
vai broker → proxy → original transparentemente. ⚠️ Só DURANTE a captura — reverter depois (senão quebra o
jogo normal, que foi o "servidor offline" desta sessão quando parei o meu world).

