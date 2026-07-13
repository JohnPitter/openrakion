# RE do gate do HIT×N nativo + formato do gamestream SE1 (task #26)

Objetivo: destravar o número nativo de HIT×N (HUD) com o bot no stage. Regra: RE completa antes de tocar código.

## 1. Cadeia de gates do `AddHitCount` (cliente, entitiesmp.dll desempacotado)

Dump: `C:/temp/entitiesmp_dump.bin` (base 0x35000000; file_off = VA−0x35000000).
`AddHitCount@0x35153ce0` (__thiscall, this=entidade do atacante, arg=damageGroup). Único caller:
`FUN_0x350d69e0` = handler de hit na VÍTIMA (`esi`=this=vítima, `edi`=atacante, passado pelo caller em `[esp+0x2c]`).

Sequência até o `call AddHitCount` @0x350d6ab6 (disasm objdump):
```
350d69fb  mov eax,[esi+0x638]; test; je 0x350d6a49   ; [vítima+0x638]!=0 -> cleanup+ret (NÃO credita)
350d6a49  cmp [esi+0x664],1;   je 0x350d6c29         ; [vítima+0x664]==1 -> pula tudo
350d6a56  mov eax,[esi+0x394]; test; je 0x350d6b1c   ; [vítima+0x394]==0 -> pula bloco de hit
350d6a6c  call 0x350d4130(vítima, edi=atacante); test eax; je 0x350d6b1c   ; check==0 -> pula
350d6a92  call 0x351dde60(eax=[esi+0x390], edx=[esi+0x38c], edi)           ; aplica o dano
350d6aa8  call ds:0x352b3630 -> eax = JOGADOR LOCAL
350d6aae  cmp edi,eax; jne 0x350d6abb                ; atacante != jogador-local -> NÃO credita
350d6ab2  push 0xa; mov ecx,edi; call 0x35153ce0     ; AddHitCount(group=0xa) no atacante
```

**Conclusão:** o combo é creditado SÓ se o atacante == jogador local E a vítima passar por 4 gates de campo
(+0x638==0, +0x664!=1, +0x394!=0, e `0x350d4130(vítima,atacante)!=0`). Medição anterior: sem bot
`AddHitCount`=7 chamadas; com bot=0 (bater num HUMANO). Logo, COM o bot, ou (a) `0x350d69e0` nem é
chamado (a detecção/simulação local do hit não roda), ou (b) um gate de campo da vítima flipou, ou (c)
o jogador-local resolve diferente. **Isso é estado de RUNTIME — não decidível estaticamente.**

### Medição decisiva (in-game, hook externo por code-cave — a via que NÃO crasha)
Estender `client/RakionDiag/capture_hitcount.cpp` p/ hookar `0x350d69e0` e logar, por chamada:
`esi` (vítima), `edi` (atacante), `[0x352b3630]()` (jogador-local), e `[esi+0x638]`, `[esi+0x664]`,
`[esi+0x394]`. Rodar 2×: (1) 2 humanos SEM bot batendo um no outro (baseline: deve chamar + creditar);
(2) 2 humanos COM bot batendo um no outro. O diff crava QUAL gate flipa → decide o fix:
- se `0x350d69e0` nem é chamado com bot ⇒ a detecção local de hit está suprimida (sim não avança) ⇒
  aponta p/ o gamestream-peer (§2);
- se é chamado mas um gate de campo/localplayer flipou ⇒ o fix é nesse campo/estado, não no gamestream.

## 2. Formato do gamestream SE1 (fonte aberta; engine.dll É a SE1)

Fonte: `Rakion/Serious-Engine/Sources/Engine/Network/`. Confirmado que o fork usa a numeração/serialização
STOCK: o disasm de `WritePlayerAction@engine.dll 0x360fbcc0` escreve `movl $0x12,(esi)` = `MSG_ACTION`=18=0x12
(bate com o enum stock contado abaixo).

### MESSAGETYPE (enum sequencial de `NetworkMessage.h`, começa em MSG_REQ_ENUMSERVERS=0; 6-bit, top 2 bits=compressão)
`MSG_ACTION`=18(0x12) · `MSG_SEQ_ALLACTIONS`=21(0x15) · `MSG_SEQ_ADDPLAYER`=22(0x16) ·
`MSG_SEQ_REMPLAYER`=23(0x17) · `MSG_GAMESTREAMBLOCKS`=26(0x1A) · `MSG_REQUESTGAMESTREAMRESEND`=27(0x1B).

### `CPlayerAction` (NetworkMessage.h:274) — 48B, ordem NÃO reordenável
`FLOAT3D pa_vTranslation`(12) + `ANGLE3D pa_aRotation`(12) + `ANGLE3D pa_aViewRotation`(12) +
`ULONG pa_ulButtons`(4) + `__int64 pa_llCreated`(8).

### Serialização da ação (delta) — `operator<<(CNetworkMessage&, CPlayerAction&)` (NetworkMessage.cpp:905), BIT-level
1. `pa_llCreated` cru: 8 bytes.
2. os 9 ULONGs de (translation×3, rotation×3, viewRotation×3): p/ cada — se 0, escreve 1 bit `0`;
   senão 1 bit `1` + 32 bits do valor.
3. `pa_ulButtons` varint-prefixo: 0→`1`(1b); 1→2b; 2-3→3b+1; 4-15→4b+4; 16-255→5b+8; 256-65535→6b+16; senão 6b+32.

### Delta-XOR — `CPlayerBuffer::CreateActionPacket` (PlayerBuffer.cpp:102)
`paDelta[i] = paCurrent[i] XOR plb_paLastAction[i]` (48B); depois `paDelta.pa_llCreated` é SUBSTITUÍDO por
`(cur.llCreated − last.llCreated)` se o cliente-destino é DONO do player, senão 0. Depois `nm << paDelta`.
Buffer vazio ⇒ REUSA a última ação (não trava). `plb_paLastAction` inicial = 0 ⇒ 1ª ação = ação cheia.

### Bloco `MSG_SEQ_ALLACTIONS` — `CServer::MakeAllActions` (Server.cpp:771)
Por sessão: `CNetworkStreamBlock nsb(MSG_SEQ_ALLACTIONS, srv_iLastProcessedSequence)`; `nsb << tick`;
p/ cada player ativo `CreateActionPacket(&nsb, iSession)` (índices de player NÃO vão no fio); `sso_nsBuffer.AddBlock(nsb)`.

### Enquadramento no fio (spec da fonte SE1, byte-a-byte)
- **`tick` = `TIME` = `float` (4 bytes), NÃO double** (Base/Types.h:123; Server.h:38). Correção crítica.
- **Bloco** (`CNetworkStreamBlock::WriteToMessage`+`InsertSubMessage`, NetworkMessage.cpp:546/259):
  `[seq:SLONG 4B LE][size:SLONG 4B LE][type:UBYTE 1B][payload]`, `size = 1+len(payload)`. Byte-alinhado (não bit).
  Payload do SEQ_ALLACTIONS = `[0x15][tick:float 4B][ CPlayerAction-delta por player ativo… ]`.
- **Envelope `MSG_GAMESTREAMBLOCKS`** (Server.cpp:406–494): `CNetworkMessage(0x1A)` + N blocos CONCATENADOS
  (SEM campo de contagem) → `PackDefault`. Default (net_iCompression=0) = SEM compressão, mas os **2 bits altos
  do byte de tipo** viram `10` → byte0 do pacote = `0x1A|(2<<6)` = **0x9A**, seguido dos bytes crus. (00=zlib,
  01=LZ, 10=raw; NetworkMessage.cpp:336). Sem CRC/ack nesta camada — ordem/perda por sequência (abaixo).
- **`MSG_SEQ_ADDPLAYER`** (0x16): `[0x16][iNewPlayer:INDEX 4B][CPlayerCharacter blob]`. Stock = nome(CTString\0)
  + team(CTString\0) + GUID(16B); **o fork sobrescreve** (blob real de 67B da captura — usar a captura, não a fonte).
- **`MSG_SEQ_REMPLAYER`** (0x17): `[0x17][iPlayer:INDEX 4B]`.

### Leitura no cliente — ORDEM ESTRITA + o SMOKING GUN
- `ProcessGameStream` (SessionState.cpp:1089): processa em ORDEM ESTRITA de sequência; buraco → **STALL** e,
  após 0.1s, `MSG_REQUESTGAMESTREAMRESEND` = `[iSeq:4][ct:4]` (:1216). Blocos fora de ordem re-sequenciam.
- `ProcessGameTick` (SessionState.cpp:936): **lê UMA `CPlayerAction` por player LOCALMENTE ATIVO**
  (`FOREACHINSTATICARRAY ses_apltPlayers … if IsActive()`), em ordem de índice — **sem contagem, sem parar no
  fim da msg**. O conjunto ativo vem dos blocos ADDPLAYER/REMPLAYER. `ApplyActionPacket` (PlayerTarget.cpp:128)
  = XOR-inverso dos 48B + `llCreated += delta`.

## 3. MECANISMO — o gamestream-peer é CAMINHO MORTO (RE estática, 2026-07-09)
Hipótese anterior (bot no conjunto ativo sem ação no SEQ_ALLACTIONS → `ProcessGameTick` desalinha) **REFUTADA
por disasm**: no Rakion os 3 handlers de gamestream do fork são STUBS vazios —
`ProcessGameTick@0x36109770`=`ret 0x8`, `ProcessGameStream@0x36109780`=`ret`, `ProcessGameStreamBlock@0x36109790`=`ret 0x4`.
⇒ o cliente Rakion **NUNCA lê o `SEQ_ALLACTIONS` per-active-player**; o gamestream agregado stock está DESLIGADO.
As ações correm SÓ por `0x030a` UNRELIABLE por-jogador (`GetActionFromMessage@0x3610afe0`), que o bot já emite.

**Consequências:**
- **Tasks #27/#28 (gamestream-peer/SEQ_ALLACTIONS) = MORTAS** — seriam lidas por ninguém. Não construir.
- O muro do HIT×N NÃO é desync de gamestream. Reancorar no **estado da entidade/sessão** (gate §1): por que,
  com o bot no roster (0x4b), um dos gates da vítima (`+0x638`/`+0x664`/`+0x394`) ou a resolução do jogador-local
  flipa ao bater num HUMANO. E, ao bater no BOT, o hit-detect local não roda porque o bot não é entidade de
  colisão client-side (limite type-7 já documentado). A única via server-side p/ entidade REMOTA de colisão é o
  `0x307 CreateNpc` (refutado p/ render-de-relay-de-fonte-não-peer — cell-monster §2.4). ⇒ avaliar se o muro é
  (a) o gate de estado (talvez corrigível) ou (b) o de colisão (arquitetural).

## 4. WIRETAP 2026-07-10 — o diff humano×bot no fio (task #29; a descoberta que reabriu a via server-side)

Instrumentação: `Network/WireTap.cs` — TSV por boot (hex integral, t_ms, mapeamento seat↔endpoint↔nome) de TODO
o tráfego que transita pelo servidor (UDP gameplay RX/TX + TCP field plaintext). Captura real: 2 humanos jogam a
partida A (sem bot, HIT×N ok) e a partida B (mesma dupla + `/addbot`, HIT×N morto).

**Achados (byte-a-byte):**
1. **Na partida A o servidor não vê NADA de gameplay** — 2 clientes na mesma máquina falam **P2P direto via
   localhost** (zero 030A/030F no servidor entre 206–520s). O baseline saudável nunca foi visível server-side; o
   `0x319` que emitimos p/ viabilizar o bot é o que sequestra o roteamento de TODOS pro servidor na partida B.
2. **Com 3+ peers no roster acorda o canal reliable do CONJUNTO** (inexistente no 2-peers): cada humano abre
   canal par-a-par `0x0304` POR PEER — push 13B `[0403][seq][src][dst][token u32 = relógio do stage ~0x28EAxxxx,
   crescente][tail=src]`, ack `0x0305` = eco com bytes 6/7 = acker — e emite âncoras `0x830C kind-01` 23B
   `[0C83][seq][src][src][01][alvo u16][2A00][9101][04000000][01000000]` (payload IDÊNTICO entre os 2 humanos —
   é protocolo, não estado; `0x0191` = classe `Classes\Player.ecl` = "entidade Player viva"): 2 cópias 1/s com
   alvo=self + salva de join com alvo em CADA membro. O master ainda emite `0x8312` = lista de peers
   `[1283][seq][src][count][seats u16...]`.
3. **O poison:** o canal humano→bot fica MEIO-ABERTO — o humano re-pusha 1/s com token NOVO (regime saudável
   humano↔humano = 5s) porque o bot nunca pusha de volta nem ancora; o `0x8312` do master e os `0x830C` de ambos
   retransmitem com **seq CONGELADO** até o fim da captura (nunca confirmados) ⇒ a ativação de combate do
   conjunto nunca fecha ⇒ HIT×N morto p/ o stage INTEIRO (inclusive humano↔humano). O ack 0305 sintetizado em
   nome do bot NÃO basta: o canal é bidirecional.
4. **Fix-1 (af5dece) REFUTADO:** fazer o bot falar o canal (push 0x0304 + âncora 0x830C). A 2ª captura (com o
   fix ligado) mostrou o bot pushando (36), os humanos ackeando os 36 pushes do bot, o bot ancorando (104) — e
   o 0x8312/0x830C dos humanos AINDA congelado. Silêncio do bot não era a causa. Revertido.

## 5. CAUSA RAIZ — o servidor SEQUESTRAVA o canal humano↔humano (fix 3505309)

A 2ª captura tem a assinatura decisiva: para TODO tipo de gameplay, `tx_para_um_humano == rx_do_OUTRO_humano
+ rx_do_bot`, exato:
```
030A: txA=1329 = rxB(866) + rxBot(463)
0311: txA=218  = rxB(164) + rxBot(54)     ← ATAQUES
830C: txA=241  = rxB(137) + rxBot(104)    ← ESTADO DE COMBATE reliable
```
O servidor estava **relayando cada pacote que os 2 humanos JÁ trocam P2P-direto** (partida A provou: sem bot,
zero tráfego no servidor). O estado reliable 0x830C/0x8312 chegando ao humano B DUAS vezes (uma direto, uma via
relay) impedia o seq/ack reliable de fechar → 0x8312/0x830C retransmitindo com seq congelado → a ativação de
combate do conjunto nunca completa → HIT×N morto p/ todos.

**Gatilho do desvio:** o `0x319` que emitíamos p/ o bot também era emitido p/ registrar o servidor como peer de
um HUMANO no outro humano (`EnsureBotEndpointRegistered(humanSeat)` em `RelayHumanToOthers` e no push handler) —
isso redireciona o canal P2P-direto deles p/ o servidor, que não preserva o seq/ack reliable. Auto-infligido.

**Fix:** o servidor SÓ injeta o bot, nunca toca no canal dos humanos:
- removido `RelayHumanToOthers` (relay humano→humano de 0x30a/0x30f/0x0311/0x83xx);
- 0x0311 humano: arbitra o hit vs os BOTS, sem relay ao outro humano (ele recebe P2P-direto);
- lockstep 0x0304/0x0305: ack só ao push endereçado a um BOT; push/ack humano↔humano = DROP;
- `0x319` só p/ seat de BOT (guard `f.RecAt(slot).IsBot`) — nunca p/ seat de humano.

**Baseline correto agora (com bot no stage):** o servidor deve ficar quase mudo entre os humanos — só 0x30a/
0x0311 do BOT relayados + os pings/echo; os pacotes que um humano manda AO BOT são consumidos p/ a IA e param ali.
Se o HIT×N ainda falhar, re-capturar e checar que NÃO há `tx_para_A` contendo pacote de B (nenhuma dupla-entrega).
