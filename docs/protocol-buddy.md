# Rakion/WolfTeam Buddy — Protocolo (reconstruído do `Buddy2.dll`)

Extraído por RE do `Buddy2.dll` (client-side; PDB
`d:\Project\WolfTeam_Buddy_dll\Buddy3\ReleaseUnicode\Buddy2.pdb` — o módulo de
buddy do WolfTeam reusado no Rakion). Ghidra: `ghidra-proj/buddy.out.txt`,
script `ghidra_scripts/BuddyProto.py`.

O servidor de buddy **não vinha** no pacote v258 e é **opcional** (o client tolera
timeout — `LEIA-ME.md`). Esta é a primeira implementação do lado servidor.

## Arquitetura (no client)
- `CBuddy2` — fachada (export `CreateBuddy2`).
- `CCommEngine` — engine TCP para o Buddy Server (FD_CONNECT, `connect`).
- `CCommP2P` — engine UDP P2P entre clients (`WSAStartup`, `sendto`); mensagens
  `P2P_SVC_*`/`P2P_RET_*` (add buddy, msg, sms, invitation, gift) — fora do servidor.
- Portas: BuddyServer **8500**, BuddyCenter **8504** (do `locale.ini`: BuddyIP/BuddyPort).

## Frame (TCP)
```
[u16 size][u16 CD][payload]      (little-endian)
size = tamanho TOTAL do pacote (inclui o campo size)
```
Parser do client (`DoProcessStream`/`OnMsg` FUN_10007420): valida `*(u16*)pkt == size`
e `size < 0x13881`; `CD = *(u16*)(pkt+2)`; payload em `pkt+4`, len = `size-4`.

## Códigos CD (RET_/NTF_ servidor→cliente, do switch do OnMsg)
| CD | Nome | CD | Nome |
|----|------|----|------|
| 0x1001 | RET_PRECREDENTIAL | 0x3001 | RET_ADD_BUDDY |
| 0x1011 | RET_LOGIN | 0x3003 | RET_REMOVE_BUDDY |
| 0x101f | NTF_VIP_IPPORT | 0x3005 | RET_GROUP_BUDDY |
| 0x1ffe | NTF_NOTICE | 0x3007 | RET_RENAME_GROUP |
| 0x1fff | NTF_CLOSE_CONNECTION | 0x3152 | RET_GROUP_GETLIST |
| 0x2010 | NTF_SAVE_PACKET | 0x3155 | RET_GROUP_DEL |
| 0x2021 | NTF_TUNNEL_PACKET | 0x3157 | RET_GROUP_CHG |
| 0x2031 | RET_SMS_SEND | 0x3fff | NTF_USER_STATE |
| 0x3151 | RET_SET_NICK | 0x5000 | NTF_NOTICE2 |

Requisições do client (`SVC_`) seguem `SVC = RET & ~1` (req par, ret ímpar):
`SVC_PRECREDENTIAL=0x1000`, `SVC_LOGIN=0x1010`, `SVC_ADD_BUDDY=0x3000`, etc.

## Handshake de login (reconstruído de FUN_10007420)
1. Client → `SVC_PRECREDENTIAL (0x1000)`. Server → `RET_PRECREDENTIAL (0x1001)` com o
   endereço externo do client (`[u32 ip][u16 port]`, exatamente 8 bytes) — usado pelo client p/ P2P.
2. Client → `SVC_LOGIN (0x1010)` (208 B AES, opaco). Server → `RET_LOGIN (0x1011)`:
   `[u16 result=0][u16 token][u16 count][count × registro 0x94]` (count @+4, registros @+6 — disasm
   @100075a4). `result==0` = sucesso; `count=0` = lista vazia. O `token` (@+2) é ecoado pelo client via
   **UDP** (sendto dos 4 bytes iniciais, @1000759e) p/ o brokering P2P aprender o endpoint.

### Registro de amigo (148 / 0x94 bytes) — loop @100075d0
| Offset | Tam | Campo | Função do client |
|--------|-----|-------|------------------|
| 0x00 | 0x14 | id ASCII (nick) | `FUN_100034f0` (copia ≤0x14, NUL-term) |
| 0x14 | 0x28 | nome UTF-16 (display) | `FUN_100097d0` (≤0x14 wide) |
| 0x3c | 0x28 | grupo UTF-16 | — |
| 0x64 | 0x30 | endereço P2P | **0 = offline** (`FUN_10009a40` registra addr 0, seguro) |

## Identidade (login cifrado → resolução por IP)
O `SVC_LOGIN` é 208 B AES-ECB opacos (chave circular: depende do próprio id) — **não dá p/ extrair o nick
do login**. Em vez disso, o **World** grava `messenger_session(account, char_name, ip)` no login (e apaga no
logout) e o Buddy resolve a conexão TCP **por IP** (`ResolveByIp`). A chave de rede do messenger é o **nick**
do char; a `buddylist` é chaveada por **account** (estável a nick change). Limitação: 2 clientes no mesmo IP
colidem na resolução — pendente IP:porta (ver `2-clientes-mesma-maquina`).

## Lista / add / delete
- **add** é MUDO no client (máscara `+0x140d4` bit12=0 → não emite `SVC_ADD_BUDDY`). Nasce no **World**: o
  client pede o account-id do nick (`0x19 CharacterGetUserName` → resposta `[0x19][0x0D][status][acct\0][nick\0]`,
  destrava "Waiting for ID Information", lang 599) e o World persiste a `buddylist` **recíproca**.
- **delete** chega ao Buddy: `SVC_REMOVE_BUDDY (0x3002)` com `[nick\0]` (RE: `P2P_SVC_REMOVEBUDDY 0xc043` →
  `FUN_10001190` envia 0x3002). O Buddy apaga os 2 sentidos e responde `RET_REMOVE_BUDDY (0x3003)`.

## Nick change (0x15, no World)
O client manda `0x15 CharacterChangeBuddyName` com `[novoNick\0]` ao **World** (não ao Buddy) e trava em "Waiting
for change request..." (lang 604). RE `FUN_004137a0`: `UPDATE usergameinfo SET buddyname` + resposta
`[u16 0x15][u16 0x0B][status][nick\0]` (tamanho strlen+6).

## Presença — `NTF_USER_STATE (0x3fff)`, parse @10008340
`[u16 count]` + por entry `[id ASCII 0x14][u8 online]`; se online, `+[ip1 4][port1 2][ip2 4][port2 2]`
(**network-order**, entry=0x21 B; offline=0x15 B). `SetUserOnline` (`FUN_100038e0`) só ativa o P2P se
`ip1==ip2 && port1==port2` → o servidor repete o **mesmo** endpoint nos 2 pares. Casada por nick (add é recíproco).

## PM e convite = P2P PURO (o servidor NÃO relaya)
As mensagens (`P2P_SVC_SEND_MSG 0xc011`) e o **convite/aceite** de amizade (`P2P_SVC_SEND_INVITATION 0xc012` /
`P2P_RET 0xc013`; strings 576/832/573/574) correm por **UDP cifrado direto cliente-a-cliente**. O servidor só
faz **brokering de endereços**: escuta UDP na mesma porta TCP, recebe o token ecoado, aprende o endpoint do
client e o repassa no `NTF_USER_STATE`. O tunnel TCP relay (`SVC 0x2020 → NTF 0x2021`) existe no protocolo mas
**não é usado** (decisão do autor: PM é P2P).

## Estado da reconstrução (validado in-process contra o DB real)
`BuddyServer` (+ `BuddyFrames` golden-tested, `BuddyDatabase`): handshake, identidade por IP, **lista** real
(RET_LOGIN), **presença** (NTF_USER_STATE), **delete** (0x3002) e **brokering P2P** (token UDP → endpoint →
endereço na presença). Lado World: handlers `0x19` (add recíproco) e `0x15` (nick change). Validado com 2
clientes simulados (127.0.0.1 / 127.0.0.2): lista, presença online/offline, delete recíproco e endpoint P2P
aprendido com pares iguais. A entrega do PM/convite em si corre no client (P2P), fora do servidor.
