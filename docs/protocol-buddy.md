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

## Convite/add de amigo = P2P (RE 2026-07-05, decisivo)

O add de amigo **NÃO** é server-authoritative no retail — é um handshake **P2P direto** (UDP cifrado
cliente-a-cliente); o servidor só broka endereços e persiste no fim. Cravado do dispatch P2P do
`Buddy2.dll` (`CCommP2P`, `switch(opcode & 0xffff)`):

| opcode | nome | papel |
|--------|------|-------|
| 0xc012 | P2P_SVC_SEND_INVITATION | A → B: "quero te adicionar" (abre o popup em B) |
| 0xc013 | P2P_RET_SEND_INVITATION | B → A: resposta do popup (aceita/recusa) |
| 0xc041 | P2P_SVC_ADDBUDDY | A → B: manda o registro completo de A |
| 0xc042 | P2P_RET_ADDBUDDY | B → A: no ACEITE (`*param_5 != 0`) o cliente chama `FUN_100011e0` = **SVC_ADD_BUDDY (0x3000) ao SERVIDOR** → persiste |
| 0xc043 | P2P_SVC_REMOVEBUDDY | delete P2P (+ SVC_REMOVE 0x3002 ao servidor) |
| 0xc011/0xc015/0xc018 | SEND_MSG/SMS/GIFT | PM/mensagens |
| 0xc051/0xc053 | NTF/SVC_STATE | presença P2P |

Senders (métodos do vtable `CCommP2P`, chamados pelo rakion.exe): invitation `FUN_10001e50`,
addbuddy `FUN_10002190`. O socket P2P binda porta **0** (efêmera, `FUN_10002dd0`: `socket(2,2,0)`+
`bind` com sa_data porta=0) e o cliente ANUNCIA a porta via `getsockname` no handler de
RET_PRECREDENTIAL (opcode 0x1b) — o endpoint P2P é a porta anunciada, **não** a efêmera do token-echo.

**Fluxo completo (retail):** F9 add → World 0x19 (lookup do account-id) → A manda P2P_SVC_SEND_INVITATION
a B (direto, endereço brokado pela presença) → popup em B → aceite (P2P_RET) → troca ADDBUDDY P2P →
no aceite CADA lado manda **SVC_ADD_BUDDY (0x3000)** ao servidor → servidor persiste + RET_ADD_BUDDY (0x3001).

**Estado atual (por que "aparece direto sem aceite"):** o P2P direto NÃO estabelece (endpoint mal brokado)
e o cliente cai no **SVC_TUNNEL_PACKET (0x2020)** = relay TCP — que o nosso server DESCARTA (regra "sem
relay"). Log empírico: 27× 0x2020, **zero** 0x3000. Sem o invitation entregue, nunca há popup/aceite; a
amizade só existe porque o World 0x19 a persiste **na hora** (atalho `BuddyService.ResolveAndAddBuddy`).
Para o aceite funcionar sem relay: brokar o endpoint P2P anunciado (getsockname, não o token-echo) +
handler real do SVC_ADD_BUDDY + tirar o atalho do 0x19. Risco: P2P na MESMA máquina (localhost) é o caso
frágil (o teste do autor é 1 PC); em 2 máquinas o P2P direto é limpo.

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

### Render do F9 — APOSENTADO o `msgfix.dll`, alvo SERVER-SIDE (2026-07-05)
> **DIRETRIZ:** nenhuma DLL injetada p/ funcionalidade — o original renderiza o F9 sem patch, logo o conserto
> é replicar a ORDEM/semântica de mensagens que o cliente espera. A `msgfix.dll` (client/RakionMsgFix) foi
> **REMOVIDA** (injeção do launcher + bundle + projeto). O "22 amigos-lixo + crash no scroll" reportado era
> provável artefato do msgfix forçando `FUN_00483600` na hora errada (o row-builder renderiza a CONTAGEM do
> store, e slots não-inicializados viram lixo). Banco + frames RET_LOGIN estão CORRETOS (verificado).

**Mapa de RE do render (fatos p/ o fix server-side):**
- `FUN_00489120` (F9 show) = **só toggle de visibilidade**; NÃO reconstrói do store.
- `FUN_00483600` (host vtable **+0x6c**) = o refresh que MONTA título + linhas. É o que o nick-change dispara.
  - título: `GetLanguageStr` + self-name via `FUN_00483850` + `SetName` `FUN_00419390`.
  - linhas: `FUN_004831b0` — ALOCA lista nova (`host+0x410`) e itera **`FUN_00489310`** (contagem do store) ×
    `FUN_00489840` (entrada i) → `FUN_00482610` (add linha). Logo nº de linhas = contagem do store.
- self-name: `host+0x44` (`std::string` VC7: buf/ptr@+0x04, `_Mysize`@+0x14, `_Myres`@+0x18). Nasce truncado
  em 2 chars porque o `@41` do 0x0C é **fixo em 2 bytes** (o resto do frame é relativo a ele); o nome completo
  vive em `AccountInfo` (`FUN_00471b70`→`[0x004d054c]`). No original o cliente monta o título completo do
  AccountInfo — falta descobrir QUANDO/por qual trigger.
- store: populado por RET_LOGIN (loop @`100075a4`, registro 0x94) E por `NTF_USER_STATE` (`FUN_10009a40`
  "Register undetermined User" ADICIONA se o nick não existe). Suspeita da causa raiz: o store **não é limpo
  ao reconectar** (re-entrar no server sem fechar o jogo) → re-login acumula. Confirmar o gatilho de clear.

**Aberto:** achar a mensagem/ordem que faz o cliente (a) montar as linhas na hora certa (RET_LOGIN chega ANTES
da janela montar → é descartado) e (b) limpar o store no relog. Pegar BASELINE NATIVO in-game (sem DLL) antes.
