# Rakion v258 — Protocolo do World Server (reconstruído do `worldserv.exe`)

Tudo aqui foi extraído por engenharia reversa do `RakionWorldServ.exe` (cópia
`worldserv.exe`) com Ghidra 11.0.3 headless — **não** de fonte da internet.
Endereços são do `worldserv.exe` v258 (ImageBase 0x400000). Ver scripts em
`rakion-work/ghidra_scripts/WSProto*.py` e saídas `ghidra-proj/wsproto*.out.txt`.

## Stack de rede
- `PerfLib::TcpSocket` / `PerfLib::UdpSocket` (IOCP / `WSARecv`/`WSARecvFrom` overlapped).
- TCP do jogo: porta `[Server] Port` = 40708. UDP gameplay: `[UDP] Port1/Port2` = 40708/40709.
- IPC com o broker: UDP (ver broker `BrokenServer`), porta IPC do world = `ipcport` (40708).

## Frame do wire (TCP, ambas as direções)
```
[u16 size][u16 A][u16 B][data...]      (little-endian)
size = 6 + len(data)        // size INCLUI o próprio campo size
```
O parser de stream lê `size`, depois `size-2` bytes de "conteúdo" = `[u16 A][u16 B][data]`.
O dispatcher recebe `(slot, opcode=A, payloadLen = (size-2)-4 = len(data), payload = &data)`.

Origem:
- Recv/parse: `FUN_0042bd70` (CWorld::Idle) → `FUN_004038d0` desenfileira `(slot,len,buf)`;
  `opcode=buf[0:2]`, `seq=buf[2:4]`, dispatch `FUN_0042ab40(this,slot,opcode,len-4,buf+4)`.
- Send: `FUN_004048e0` escreve `[u16 size=len+2][conteúdo]` no socket.
- Send (fila): `FUN_0041b940` → `FUN_0042e630`/`FUN_0042e720` enfileira `[u16 slot][u16 len][conteúdo]`,
  drenado depois para `FUN_004048e0`.

### Cliente → servidor
`A = opcode`, `B = seq`. **Sequência validada** (`FUN_0042bd70`): `seq == últimoSeq+1`
(reseta para 0 no opcode `0x0C` login; wrap quando passa de 65000). Exceções sem checagem
de seq: `0x0C` (login) e `0x0F`. Seq inválida → `Disconnect(reason 2)`.
Contador por usuário em `user+0x146e`.

### Servidor → cliente
`A = serverSeq` (contador por usuário em `user+0x1488`, **incrementado a cada envio** em
`FUN_0041b940`), `B = msgType`. msgType conhecidos:
- `2` = LoginComplete (sucesso do login, `FUN_0041f6c0`).
- `4` = Disconnect notify (`FUN_0041eb20`).

## Eventos de conexão (opcode 0)
Pacotes internos com `A=0`; `byte[2]` (= B low) é o tipo de evento:
- `0` = Connect → `FUN_0040da80(user, …)` (marca `user+0x1440` = "em canal").
- `1` = Disconnect/idle → `FUN_0041eb20(slot, 1, …)`.
- `2`/outros = erros de I/O (IOE2/IOE3).
Mensagens normais só são dispatchadas se `user+0x1440 != 0` (conectado).

## Dispatcher `FUN_0042ab40(this, slot, opcode, len, payload)`
`switch(opcode)` com cases `0x01..0x79` → handlers (tabela em `Protocol.KnownOpcodes`).
`default` → `Disconnect(reason 0xC9 = 201)`.
Após o switch: `if (this+0x5b18 == 0)` (não autenticado) → `FUN_0041f6c0` (LOGIN);
senão → `FUN_0042a310` (in-game). O opcode que cai nesse fallthrough é o `0x0C`
(único `case 0xc: break;`).

## Login — opcode `0x0C` → `FUN_0041f6c0`
Payload: `[u8 connType][userID\0][field2\0][field3\0][u16 tail]`.
- `connType`: `4`=Normal (pula checagem de nome de sessão); `1`=PK (compara nome com
  `this+0x14d`); outros comparam com `this+0x12c`.
- Guards (na ordem):
  1. `this+0x50 != 0` → `Disconnect(0x12)` (servidor travado).
  2. `user+0x1460 != 0 || user+0x14a4 != 0` → `Disconnect(0x13)` (login duplicado).
  3. `curUsers >= this+0x536c` → erro `cat 3, {0x0C, 10}` (servidor cheio).
  4. nome != sessão (se connType != 4) → erro `cat 3, {0x0C, 8}` (= "ID doesn't exist").
  5. `len(field2) >= 0x11` (17) → `Disconnect(0x14)`  ← o famoso "DISC 020".
  6. `len(field3) >= 0x15` (21) → `Disconnect(0x15)`.
- Sucesso: grava campos (`FUN_0041b810`), envia LoginComplete (msgType 2):
  `[u16 serverSeq][u16 2][field2\0][field3\0][u16 tail][u32 ...][u8 1]`, incrementa contador
  de sessão.

## Disconnect — `FUN_0041eb20(this, slot, reason, sendText, flag)`
Loga `"[%04u] DISC %03u"` (slot, reason). Se o usuário está em canal, envia notify
msgType 4: `[u16 serverSeq][u16 4][u32 user+0x1468][u16 reason][u32 ...]`. Razões nomeadas
em logs: `0x47`=FieldList, `0x4f`=FieldQuickEnter, `0x51`=FieldExit, `0x53`=FieldCreate,
`0x61`=FieldReady, `0x7f`=FieldChat.

## Field Invitation (convite de sala) — opcode `0x72` (RE 2026-07-05, dois lados)
Fluxo do botão **Invite** da sala (2 clientes). Cadeia: diálogo → SEND → relay/NOTIFY → popup → aceite.

**1) Diálogo (já portado):** o clique manda `0x1E` ao world → resposta = user list do canal
(`LobbyFrames.ChannelList`), que popula a lista de convidáveis.

**2) SEND (master → world):** `IScavengerWorldNet::SendFieldInvitation(u16)` @`engine.dll 0x36191af0`
escreve **`[72 00][targetUserId:u16]`** (4B; resto do bloco = pad). `targetUserId` = o UserId da user
list = `GameInfoId` (>0) senão slot.

**3) Relay/NOTIFY (world → ALVO):** dispatcher `FUN_0042ab40` caso `0x72` → **`FUN_00428520`**:
- valida o master: `+0x1460 && +0x14a4` (ativo/em field) senão `0xd6`; `+0x1440==3` (em sala) senão `0xd7`.
- valida o alvo: `target+0x1460 != 0` (online) senão `0xd8`. **Erro ⇒ `FUN_0041eb20` DESCONECTA o master.**
- monta e envia ao alvo (`FUN_004038e0`, dest = targetId):
  `[72 00][inviterId:u16][inviterName\0][fieldSlot:u16][blob]`, com `inviterId`=slot do master,
  `inviterName`=`master+0x14a8`, `fieldSlot`=`master+0x14a0` (índice do field), `blob`=`FUN_00406a80`
  (map `+0x118`, mode `+0x119`, minLvl `+0x111`, maxLvl `+0x112`, `+0x113`, maxRounds `+0x11a`, `u16 +0x11c`,
  `roomName\0`, `roomName2\0`).

**4) Popup (cliente-alvo):** `ProcessWorldRecvBuffer` @`0x36197a40` drena a fila → `FUN_36197320(op,data,len)`
→ caso `0x72` = **`FUN_36193f40`**. Lê, do payload:
`[inviterId:u16][inviterName\0][fieldSlot:u16][map][mode][minLvl][maxLvl][0][maxRounds][0][roomName\0][pass\0]`
(reancorra pelo NUL do nome via `lstrlen`), e chama o host-callback `vtable+0x260` = popup.

**5) Aceite:** o cliente ecoa `fieldSlot` via `SendFieldEnter` = **`0x38`** (join, já portado). Sem opcode novo.

> ⚠️ **NOTA-DE-BUILD (rakion-final ≠ rakion-new):** o `engine.dll` (parser do frame) lê **7 bytes** de
> atributos entre `fieldSlot` e `roomName`; o `worldserv.exe` do RE emite **8** (6 + `u16 +0x11c`). Como quem
> parseia o nosso frame é o `engine.dll`, a síntese segue os **7 bytes**. Implementação: `LobbyFrames.FieldInvitation`
> (síntese pura do `Field` — sem replay) + `ClientSession.HandleFieldInvite` (relay; erro = **drop silencioso**,
> não desconecta) + `WorldServer.GetSessionByUserId`. Golden: `FieldInvitation_ByteExact_MatchesClientParse`.

## Tabela de opcodes (cases do dispatcher)
`0x01..0x05, 0x08..0x0C, 0x0E..0x10, 0x12..0x1C, 0x1E, 0x20, 0x22, 0x29..0x2F, 0x31..0x36,
0x38..0x3B, 0x3D..0x43, 0x45..0x4D, 0x4F, 0x50, 0x53, 0x56, 0x57, 0x59..0x5B, 0x5D, 0x5E,
0x60..0x62, 0x64, 0x65, 0x6B..0x79, 0x7F`. Nomes conhecidos das strings do exe estão em
`Protocol.Op`. Handlers ainda não reconstruídos são logados e marcados como stub (cada um
referencia o `FUN_xxxx` de origem para RE incremental).

## Cifra de pacote (canal "lobby") — AES
O envio do lobby (`FUN_004038e0`) empacota o payload em blocos de **12 bytes** e expande
para **16** (`FUN_00401040`), chamando `FUN_00401670` por bloco. `FUN_00401670` é **AES**
clássico baseado em T-tables: 4 tabelas de 256×4 bytes em `0x442548 / 0x442948 / 0x442d48 /
0x443148`, round-keys a partir de `ctx+8`, nº de rounds em `ctx+4` (**10/12/14** = AES-128/192/256),
e o flag de "cripto ligada" em `ctx+0x208 & 1` (quando 0, `FUN_00401670` retorna sem cifrar).
O bloco AES de entrada = `[4 bytes de ctx+0x20c (IV/contador)] + [12 bytes de plaintext]`.

**Key-setup (RESOLVIDO por RE):** o construtor `FUN_00401000(ctx, key)` faz `ctx+0x208=0` (cifra
off), `ctx+0x20c=0xc47f` (IV/seed) e chama `FUN_00401200(key, 0x10, 3, ctx)` = key-expansion
Rijndael (Rcon `DAT_004424d0`) → **AES-128, 10 rounds**. A **chave é HARDCODED** (16 bytes), montada
em `FUN_00403c10` (`local_20…`):

    E1 3A 7E F5 37 2C 10 4D 4E CE B3 0C 56 26 A4 8E   (IV seed 0xc47f)

Bloco AES = `[4B IV (0xc47f LE)] + [12B plaintext]`. Implementado em `RakionServer.Common.PacketCrypto`
(`WorldKey`/`WorldIv`/`EnableWorldDefault()`).

**RESOLVIDO (ligado e verificado):**
- O flag `ctx+0x208` é ligado **no setup da conexão**: `FUN_00401200(...,3,ctx)` faz `ctx+0x208=(0^3)&3=3`.
  Replicado: `ClientSession.Start()` chama `Crypto.EnableWorldDefault()`.
- **IV é FIXO** (0xc47f) em todo bloco — `FUN_00401670` só escreve a saída (`param_2`), não reencadeia
  o `param_1`/IV. (Bug corrigido no `PacketCrypto`: era `iv+i`, agora constante.)
- `SendLobby` cifra o payload (pad→12, AES→16) e enquadra `[u16 size][cipher]`.
- **Verificação independente:** o pacote de lobby cifrado pelo servidor, decifrado por AES externo
  (pycryptodome) com a chave, devolve `7f c4 00 00 | [u16 subtype][payload]` byte a byte. Há um
  `PacketCrypto.SelfTest()` no boot (roundtrip Encrypt/Decrypt).
- Inbound das mensagens validadas vai em texto (dispatcher lê opcode/seq direto); a cifra é do
  canal lobby/field **outbound**.

## Handlers (dispatcher FUN_0042ab40)
Os 87 handlers estão mapeados em `Network/WorldHandlers.cs` (opcode → método nomeado + endereço
`FUN_xxxx`). Reconstruídos e validados: `0x01 EnterChannel`, `0x02 RequestWorldInfo`,
`0x03 GmServerOpenClose`. Os demais são `Stub()` (logam e citam o `FUN_xxxx`) — preencher = nova
função. Decompilação de referência de todos: `ghidra-proj/handlers.out.txt`.

## Banco (db `rakion`, MySQL/MariaDB)
Login direto (Auth.Type=0) lê `user(id,password,Authority,country)`. Pós-login:
`usergameinfo`, `characterinfo`, `loguserconnect` (INSERT da conexão), `usercount`,
`cash`. Schemas em `rakion-tutorial/server/DB/rakion_all.sql`.

## Canal IPC broker ↔ world (UDP)
Do broker `BrokenServer` (`Program.OnIPC` + `Network/Servers.cs`):
- world → broker "ServerInfo": `[u16 257][u8 rnd][u8 2][u16 len][u8 serverId][u16 maxSalas]
  [u16 usedSala][u16 maxSlots][u16 usedSlots][u8 crc]` → broker marca "online".
- broker → world `RequestServerInfo (cmd 0)` / `RequestLogin (cmd 1)`; world responde
  `ResponseLogin (cmd 3)`. CRC = XOR de todos os bytes. Cifra opcional (campo `code`).

## Inventário → Previous (capturado do worldserv ORIGINAL via mitm, 2026-06-11)
Captura passthrough (cliente ↔ original sob Wine) do fluxo login → inventário → Previous. O cliente
fica em **char-select** (não na lista de salas) se o servidor não replicar estes acks — antes
remandávamos `0x13`/`0x31` a cada poll, o cliente reprocessava o grid de widgets, ficava em polling
(telas sobrepostas) e o Previous caía no char-select. Frames W→C corretos (`ClientSession.cs`):

| Opcode | Resposta do original | Notas |
|--------|----------------------|-------|
| `0x2c` (enter) | `[2c 00][00][handle:4][00 01][00 12][00]` | **handle de sessão** = bytes 13..16 do `0x0C` de login. NÃO ecoa o body do cliente. NÃO manda `0x31` aqui. |
| `0x2d` (list) | `[2d 00][00 00][2c 00 00][handle:4][00]` | SEMPRE este ack — NUNCA a lista `0x13`. Remandar `0x13` a cada poll prende o cliente. |
| `0x36` | `_r36` + `_r36b` só **1×** | `_r36b` arma a lista de games (botão Create); remandar a cada poll re-arma e mantém o polling. |

- **Box (grid):** no cliente GG-removido o grid só pinta via `0x31` (FUN_0047d1d0), nunca via `0x13`.
  Mandar `0x31` por item **uma vez** na abertura (handler `0x2c`) pinta o box sem re-quebrar o Previous
  (a compra `0x2e` já re-pinta com `0x31`). `0x13` sozinho não renderiza.
- **Handles NÃO são copiáveis** do original: o `e703` (=user id 999 do nosso `test`) em `_r1f`/`_r1e` é
  VALIDADO pelo cliente — trocar por `0000` (como na captura, onde o `test` do container é id 1) trava
  o cliente re-mandando `0x14` no char-select. Da captura aproveita-se só a **estrutura**, com handles nossos.

### Quickslot de poção (0x31) — persistência e a forma SEGURA do repaint
O move box↔quickslot (`0x31` C→S) é só estado em memória no original (arrays `user+0x1e2c` box /
`user+0x1da4` quickslot, persistidos via `UserItemInfo.slot`). No nosso modelo o box é a `itembox`;
a posição persiste na coluna **`itembox.qslot`** (0 = box, N = célula N−1 do quickslot; provisionada
no boot por `EnsureSchemaAsync`). Cada move reconcilia por itemId (`SaveQuickslotAsync` — poções do
mesmo id são fungíveis); no login `LoadQuickslotAsync` repõe o quickslot e o box exclui `qslot>0`.

**Repaint na abertura — regras aprendidas de um crash real** (`rakion.bin+0x407ed`, AV no draw
`FUN_00440770`, que deref a definição do item SEM null-check):
1. **A origem do frame 0x31 deve SEMPRE ser type=0 (box)** — o caminho de origem type=1 do handler do
   cliente (`FUN_0047d1d0`) escreve num array de widgets indexado pela célula **sem bounds-check**
   (`+0x22c + célula*4`); com as células 13..15 (as que o cliente usa na barra) corrompe widgets
   vizinhos e o draw seguinte crasha. Repaint correto: origem = 1ª célula VAZIA do box (item 0,
   no-op visual), destino = célula do quickslot — a MESMA forma do move ao vivo, única validada.
2. **Nunca pintar célula com item 0** no box (frame nunca observado do original).
3. **Pintar o quickslot só no 1º `0x2c` da sessão** — nas reentradas o cliente já tem o estado local
   (ele processou os próprios moves); repintar é redundante.
- Captura do original: imagem `openrakion-server:latest` (container do `rakion-tutorial`, Wine win32 com
  GG funcionando) + mitm passthrough `41708→40708`. A `rakion-spike:e2e` é a versão quebrada (GG falha).
