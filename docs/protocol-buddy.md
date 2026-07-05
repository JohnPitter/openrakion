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
   endereço externo do client (`[u32 ip][u16 port]`) — usado pelo client p/ P2P (getsockname).
2. Client → `SVC_LOGIN (0x1010)`. Server → `RET_LOGIN (0x1011)`:
   - payload `[u16 result]...`; `result==0` = sucesso. Para o caminho de sucesso o
     payload precisa ter **> 7 bytes**; `[u16 buddyCount]` (cap 500) seguido de N
     registros de **148 (0x94) bytes** (nome unicode, guild, ext, IP/port). `buddyCount=0`
     → lista vazia e o client conclui ("RET_LOGIN END").

## Estado da reconstrução
Implementado e validado (`BuddyServer`): framing, tabela de CD, e o handshake
PRECREDENTIAL→LOGIN (login OK com lista vazia → client prossegue). Os demais CDs
são logados como stub (ADD/REMOVE buddy, grupos, SMS, tunnel) — cada um precisa do
layout de payload (RE incremental do handler correspondente no OnMsg). O canal P2P
(UDP, `P2P_SVC_*`) é entre clients e não passa pelo servidor.

## Render da janela F9 (RE client-side `rakion.exe`, ImageBase 0x400000) — 2026-07-04

**Por que a lista/título nasce VAZIA na abertura e só popula após um "nick change".**
Cadeia do F9, cravada por decomp (cliproj/rakion_orig.exe):

- **Criação (login, NÃO F9):** `FUN_0047bce0` (handler da resposta de login do World) chama
  `FUN_0040bf90`, que **cria a janela do messenger + o host CBuddy2** (que conecta ao Buddy),
  e a deixa **oculta** (`+0x128` vtable `+0x54` = `csComponent::Hide`). Objeto vive em
  `outer+0x4a60` (`DAT_004feed0+0x4a60`); getter = `FUN_0040b9b0`.
- **Toggle F9:** `FUN_0040e8e0` (key handler) em `WM_KEYUP(0x101)` + `VK_F9(0x78)` →
  `FUN_00482020(GetMessenger())` → `FUN_00489120` (flip do byte de visível em `+0x124`).
  No SHOW chama só: vtable `+0x88` `FUN_00482ec0` (`csComponent::IsShow(+0x128,1)` + `Select`)
  e `FUN_004890c0` (itera os widgets-filho em `+0xd4` chamando `+0x10`). Depois `FUN_0040bc10`
  (pause/resume do jogo).
- **NENHUM** desses reconstrói a lista a partir do store do CBuddy2. As linhas e o contador
  de online só entram por **eventos assíncronos**:
  - presença `FUN_00489590` (host cb): acha o buddy (`FUN_004891c0`), seta flag online em
    `+0x54`, ajusta o **contador de online em `host+0xe8`** (1º número do título "on/total"),
    adiciona à lista visível (`FUN_00488d10`), repintando (vtable `+0x78`).
  - nick/list callbacks → primitiva de add `FUN_00489c90` (switch por categoria de grupo).

**Consequência:** o `RET_LOGIN` (registra o roster, byte-perfeito) e o `NTF_USER_STATE`
(acende) chegam no **login**, ANTES do F9. A janela, ao abrir, só reexibe os widgets que já
existem — não relê o store. O "nick change" funciona porque dispara um evento tardio (janela
já montada) que força o rebuild/repaint. **O servidor não tem sinal de F9-open** (o SHOW é
100% client-side `csComponent`), então push server-side não conserta (e re-push de presença
OFFLINE *desregistra* — `FUN_10009aa0` "Unregister User" — esvaziando a lista).

**Fix = patch no client** (mesmo mecanismo do botão Add Bot / janela): fazer o caminho de SHOW
(`FUN_00489120` / site do F9 em `0x40e8e0`) forçar rebuild-a-partir-do-store + repaint. Aberto:
distinguir **modelo-vazio** (re-popular via `FUN_00489c90` iterando o store) de **só-repaint**
(dado presente, falta invalidar) — resolver com diagnóstico em runtime antes de gravar bytes
(patches cegos aqui têm histórico de AV).

### RESOLVIDO — `msgfix.dll` (client/RakionMsgFix, 2026-07-05, validado in-game)
Diagnóstico runtime cravou: no SHOW o modelo estava **vazio** (não é só-repaint), e o self-name chega
**truncado em 2 chars** (`"Go"`) porque o campo do 0x0C (`@41` do `LoginCharListWriter`) é fixo em 2 bytes.
O patch (`msgfix.dll`, native x86, injetado pelo launcher — injetar por fora TRAVA o jogo) hooka
`FUN_00489120` e no **1º SHOW**:
1. lê o prefixo de 2 chars do self-name (`host+0x44`, `std::string` VC7 — buf/ptr@+0x04, `_Mysize`@+0x14,
   `_Myres`@+0x18; SSO se `_Myres<=15`);
2. resolve `AccountInfo` (`FUN_00471b70` → `[0x004d054c]` `GetAccountInfo`, `__thiscall`) e acha a 1ª string
   alfanumérica que **começa com o prefixo e é mais longa** (`"GoHeroi"`, auto-validante), gravando-a em
   `host+0x44`;
3. chama `FUN_00483600(host)` (host vtable **+0x6c**) — o MESMO refresh do nick-change — que monta o título
   (`GetLanguageStr` + self-name via `FUN_00483850` + `SetName` `FUN_00419390`) e reconstrói as linhas
   (`FUN_004831b0`, inclusive amigos offline).

Resultado: F9 abre com `GoHeroi's Rakion messenger [0/N]` + lista completa na hora, sem nick-change. 1x por
sessão (o título persiste; evita realocar o widget de lista). Prólogo do hook: `56 8b f1 8a 46 24` (6B).
