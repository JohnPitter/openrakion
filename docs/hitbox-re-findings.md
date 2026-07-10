# RE do HIT×N — por que o golpe do humano não conta no bot promovido

> 2026-07-06. Depois que o bot virou **entidade física** no cliente do humano (endereço P2P no
> registro do roster → promoção; sintoma in-game: **CP conta** e o bot **é repelido** ao encostar),
> o **HIT×N nativo ainda não incrementa** e o **golpe não anima**. Esta é a RE da `engine.dll`
> (SE1, símbolos, ImageBase 0x36000000, ESTÁVEL entre builds — a `entitiesmp/gamemp` do rakion-final
> são EMPACOTADAS, offsets não transferem: [[rakion-final-binario-diferente-re-ao-vivo]]) que cravou
> a arquitetura da entrega e isolou os dois muros restantes.

## Confirmação de impacto no P2P real e descarte do atalho 0x315 (2026-07-10)

A análise do `rakion.bin` usado pelo cliente em `rakion-final` encontrou uma segunda entrada para o
contador, independente de `ReceiveDamage`. O dispatcher de gameplay `FUN_00411760@0x411760` valida
primeiro o remetente com `CSessionState::IsValidUDP_ForPlayer` e então trata:

```text
case 0x315:
    message >> hitKind;
    local = FieldInfo::GetLocalPlayer();
    if (local != null) CPlayer::AddHitCount(local, hitKind);
```

O `ReceiveDamage` chama o mesmo `AddHitCount` com `hitKind=0x0A`, mas a captura golden humano↔humano não
usa `0x315`. O encadeamento observado no fio é `0x830c → 0x0311 → 0x8315`: `0x0311` inicia/propaga o ataque
e o `0x8315` reliable posterior confirma o impacto. Cada `0x83xx` entregue recebe um ACK `0x4000`, cujo
`u32` no offset 7 referencia a sequência confirmada e precisa voltar ao peer que enviou o reliable.

Enviar `0x315` ao receber apenas o `0x311` foi descartado: alcance/cone no instante da animação fazia o
contador aparecer sem o golpe atravessar o bot. O teste ao vivo confirmou que o cliente nunca produz o
`0x0311` de 12 bytes nem o `0x8315` contra o avatar type-7, portanto esses frames não podem confirmar o bot.

A confirmação server-side usa a combinação que o cliente realmente fornece: o `0x0311` de 10 bytes abre uma
única tentativa e o próximo `0x30a` entrega o vetor `aimX/aimZ` do golpe. O World testa esse segmento contra
os corpos inimigos e só aplica dano se o bot for o primeiro corpo atravessado. Proximidade sem interseção não
conta. O `0x315` é enviado ao atacante somente depois dessa colisão server-side, para atualizar o HUD.

O handler `0x315` continua sendo um achado válido de RE, mas não é usado como confirmação de contato. Ele
incrementa um contador diretamente, sem provar colisão, faísca, reação ou dano no alvo.

Além da morte humana fabricada, havia uma falha no hub UDP: o World consumia `0x8315` e `0x4000` como se fossem
feedback/input local. Com bot no stage, isso deixava o `0x8312` retransmitindo sem ACK e impedia a ativação
normal do combate entre humanos. O World agora relaya `0x8315` e roteia cada `0x4000` ao remetente original.

## Mapa cravado (tudo em engine.dll, RVAs reais via EAT)

| símbolo | VA | papel |
|---|---|---|
| `CSessionState::HandleMessage` | **0x3610d7c0** | dispatcher das msgs reliable de entidade **0x307–0x312** (jump table @0x3610e280) |
| `CSessionState::GetActionFromMessage` | 0x3610afe0 | decode do 0x30a (movimento) — funciona (o bot anda) |
| `CSessionState::AddRemoteGeneralNpc` | 0x361097a0 | cria NPC remoto (Cell) — entidade REAL de colisão; **caso 0x307** |
| `CSessionState::AddRemotePlayer` | 0x3610e2b0 | cria PLAYER remoto real — **sem caller interno na engine** |
| `CSessionState::GetPlayer` | (0x07e8) | resolve entidade de jogador por seat |
| aplicador de estado | 0x3610d350 | roteia por TIPO: **tipo 1 → tabela PLAYER `+0x1d20`**; tipo 2 → NPC `+0x1d70` |

### O dispatcher HandleMessage (0x307–0x312)
`msgtype - 0x307`, `cmp <= 0xb`, `jmp [tabela@0x3610e280]`. Casos mapeados:
- **0x307** (create general NPC) → `AddRemoteGeneralNpc`
- **0x30c** (= nosso alive-flag `0x830c`) → 0x3610db7f → parseia `[A][len][C][D][u32][u32][blob]` →
  chama o aplicador **0x3610d350** (roteia p/ a tabela PLAYER `+0x1d20` quando tipo=1)
- 0x30d/0x30e/0x311 → default (não tratados aqui; 0x311 golpe é unreliable, tratado noutra via)

**Confirmação-chave:** nossa síntese do `0x830c` alive-flag é **byte-idêntica** ao que o joiner humano
manda na captura (l.285: `0c83…0a0a010a002a0091010400000001000000`) — o teste `AliveFlagDatagram_*`
trava isso. Ou seja: **o formato NÃO é o problema.**

## Os DOIS muros (por que ainda não conta)

### Muro 1 — a entrega reliable (validação de peer/sequência)
Se a captura humano↔humano usa **exatamente** os mesmos bytes e lá o HIT×N funciona, mas o nosso
(byte-idêntico) não, a diferença está na **camada de entrega reliable**, não no conteúdo:
- As msgs `0x83xx` são **reliable** (bit 0x8000) — o host mantém **sequência + ACK por-peer** e
  **valida o remetente** antes de entregar ao `HandleMessage`.
- Nós mandamos o `0x830c` como datagrama avulso **relayado pelo servidor** (origem = socket do
  servidor), com **seq compartilhada com os 0x30a** (unreliable). O host provavelmente **descarta**
  antes do dispatcher: sequência reliable fora de ordem / peer não-validado.
- O lockstep `0x0304/0x0305` que fechamos é o **transporte** reliable (open/push/ack); o `0x830c` é
  uma **mensagem de aplicação** reliable que precisa entrar NESSA janela com seq/ack próprios —
  subsistema que ainda não sintetizamos (só o keepalive do canal).

### Muro 2 — o create do PLAYER real é código empacotado
`AddRemotePlayer` (0x3610e2b0) **não tem caller dentro da engine.dll** — é invocado pela `gamemp/
entitiesmp` EMPACOTADAS (o mesmo muro do headless §12). Nosso `0x4b` cria um avatar que **anda +
colide** (por isso a repulsão), mas provavelmente uma entidade **local-física**, não o **peer remoto
cinemático** que `AddRemotePlayer` produz — e é o peer remoto que entra na tabela `+0x1d20` como
**combatente hittável** (ENF_ALIVE + team + HP, o gate do `AddHitCount`, cell-monster-re §5).

## Conclusão honesta
A promoção via endereço no roster foi real e mensurável (colisão física + CP). Mas o **HIT×N nativo**
exige a entidade do bot ser um **combatente de sessão completo** na tabela `+0x1d20`, e as duas vias
para isso batem em muro:
1. **Injetar o estado de combate** via a msg reliable `0x30c` → precisa reversar o **subsistema de
   mensagens reliable** (seq/ack/validação de peer) p/ o host aceitar nossos frames. Sub-projeto.
2. **Criar o peer real** via `AddRemotePlayer` → código empacotado. Mesmo muro do headless.

Nenhum é um ajuste de bytes; ambos são frentes de investigação próprias. O que ESTÁ entregue e
funcional: bot que anda (0x30a nativo), colide (entidade física), leva dano/morre/respawna
(arbitragem server-side), placar/vitória. O contador nativo e a animação de golpe ficam atrás desses
dois muros.

## O gate EXATO (cell-monster-re §5) — por que a entidade física não basta
`AddHitCount` roda DENTRO da `ReceiveDamage` da vítima, no cliente do ATACANTE, só se **todos** os
gates passarem — e são campos de ESTADO da entidade (entitiesmp, empacotado):

| gate | campo | o bot promovido tem? |
|---|---|---|
| `ENF_ALIVE` | flags +0x10 | **incerto** (setado no init de CPlayer real) |
| não-morrendo | +0x638 == 0 | ? |
| **não-template** | **+0x664 != 1** | **SUSPEITO**: avatar sintético pode nascer template/placeholder → `ReceiveDamage` sai na hora |
| ativo | +0x394 != 0 | ? |
| **team inimigo** | **+0x26c ∈ {0x0a,0x14}**, 0 FALHA | **SUSPEITO**: 0x4b não seta team explícito → 0/neutro → IsEnemy falha |
| HP > 0 | +0x624 | ? |

A **repulsão prova só a colisão de MOVIMENTO** — nenhum desses gates de COMBATE. Os campos são
inicializados no path de CPlayer real (via `AddRemotePlayer`, empacotado) — nosso `0x4b` cria o corpo
mas não roda esse init, então team/alive/template/active ficam por sorte. É por isso que o corpo
existe (repele) mas o raycast do golpe, mesmo achando o corpo, **sai no primeiro gate** (template ou
   team=0) → zero HIT×N pelo caminho de dano. O `0x315` acima é apenas um handler diagnóstico e não substitui
   o estado de combatente nem a confirmação real de contato.

## ATAQUE AOS DOIS MUROS (RE 2026-07-06, sessão longa) — cravado na engine.dll

### Muro 1 — o gate reliable DECODIFICADO (`IsApplyReliableUDP@0x36109e20`)
Decide se um reliable-UDP é APLICADO. **Dois gates em AND:**

1. **`IsValidUDP_ForPlayer`@0x36109da0** — base da tabela de players (stride **0x378**/player);
   confere `[base + seat*0x378 + 0x1e8] == IP_remetente` **E** `[+0x1ec] == porta_remetente`. É o
   MESMO gate do 0x30a — **já passamos** (o 0x319 registra o socket do servidor como endpoint do seat).
2. **Gate de SEQUÊNCIA**@0x36109de0 — `seq_recebido > [base + seat*0x378 + 0x1f4]` (`jbe → rejeita`);
   on-pass grava `[+0x1f4] = seq`. **Sequência estritamente monotônica por assento.**

⇒ Um reliable do bot (0x830c/0x307) é aceito SE: (a) vem do endpoint registrado (ok via 0x319) E
(b) tem `u32 seq` (offset +2 do frame) estritamente crescente. Nosso `bot.UdpSeq++` já é monotônico,
então **o 0x830c provavelmente JÁ passa os dois gates** — logo o Muro 1 **não é** o bloqueio do
alive-flag. O bloqueio é o Muro 2 (a entidade não é combatente; o alive-flag só ATUALIZA estado de
uma entidade que nunca foi criada como combatente).

### Muro 2 — o create do combatente + as funções de setup (RVAs no binário REAL)
`AddRemotePlayer@0x3610e2b0` **não é referenciado em .text/.rdata/.data** da engine → chamado 100%
do código EMPACOTADO (gamemp/entitiesmp). As funções que ELE encadeia p/ montar o combatente SÃO
exports da engine (rakion-final), confirmadas:

| função | VA | seta |
|---|---|---|
| `ChangeTeam@CSessionState` | **0x36109f00** | team **+0x26c** (gate IsEnemy) |
| `SetAsRemoteEntity@CEntity` | **0x3600cfd0** | marca REMOTA/cinemática (≠ local-física que repele) |
| `GetPlayersCount@CSessionState` | 0x36109... | contagem de players networked |

A `entitiesmp` desempacotada (rakion-new, versão MAIS NOVA — offsets não transferem, LÓGICA sim)
importa da engine um cluster de setup: `SetAsRemoteEntity`, `ChangeTeam`, `RecvCreateCreature`,
`InputLocalClientCreatureNpcSlot`, `GetPlayersCount`, `CreateEventCreature`. ⇒ o combatente é montado
pela `entitiesmp` chamando esses exports quando processa o **player-add de SESSÃO** (não o 0x4b de
field). Nosso bot está na tabela de FIELD (0x4b) mas não na lista de players de SESSÃO → a engine
nunca roda o setup de combatente nele.

### O CAMINHO que a RE abriu — `0x307` create-NPC via reliable (100% engine, dirigível server-side)
O dispatcher `HandleMessage@0x3610d7c0` (msgtypes 0x307–0x312) tem o **caso 0x307 → `AddRemoteGeneralNpc`
@0x361097a0 → `CWorld::CreateNpc`**: cria um **`CNpc*` REAL com colisão e HP** (o molde do Cell,
cell-monster-re) — TUDO dentro da engine, sem código empacotado. Tentamos 0x307 antes e "não
renderizou" — mas agora sabemos o porquê provável: **não passava o gate de sequência reliable**
(mandávamos unreliable/seq errada). Com o gate decodificado, o experimento correto é:

> Mandar o `0x307` (create-NPC, codec `BuildCreateNpcDatagram` já existe) como **reliable do endpoint
> registrado, com `u32 seq` monotônico** que bata em `IsApplyReliableUDP`. Se passar → NPC nativo
> hittável (HIT×N vem de graça, cell-monster §5), dirigido server-side por 0x30f (owner*9+sub).
> Tradeoff conhecido: o bot aparece como monstro (Cell), não como player ([[bot-hittability-type7-verdict]]).

**Owner do 0x307** = índice de player-slot válido (indexa `owner*9`, sem bounds-check — cell-monster
§0.1a); usar o seat do bot. Blob = os 43B golden da captura de Cell.

### Veredito da dupla-frente
- **Muro 1**: gate reliable DECODIFICADO e satisfazível (endpoint + seq monotônica). Não era o
  bloqueio do alive-flag.
- **Muro 2**: create de combatente-PLAYER é empacotado (fechado por ora), MAS o create de
  combatente-NPC (0x307) é in-engine e agora tem caminho de entrega claro (reliable+seq). É o
  ataque viável.

## AddRemotePlayer POR DENTRO (rakion-new desempacotada @0x3612f8f0) — como um combatente nasce
Reversado o corpo (versão nova, lógica transfere):
```
AddRemotePlayer(seat, u16, char* name):
  GetGlobalFieldInfo()
  AddPlayer@CSessionState(...)        ; <-- CRIA a entidade na TABELA DE PLAYERS DE SESSÃO (+0x1d20)
  CNetMessage(); Write(...)           ; serializa
  GetPlayer@CSessionState(seat) x2    ; recupera a entidade criada
  SendInfoCreateNpcTo(...)            ; propaga o create (reliable) aos outros
```
⇒ O combatente é **`CSessionState::AddPlayer`** (export da engine) — dispara no player-add de
**SESSÃO**, não no `0x4b` de field. Nosso bot está só na tabela de FIELD → a engine nunca roda esse
caminho nele → entidade sem estado de combatente.

`SendInfoCreateNpcTo`@rakion-new: **`SendPacket_Reliable`** (create É reliable), checa
`EntityExists@CWorld` e serializa a entidade pela **vtable** (`call [ent+0x1b8]`) — o "blob" É a
entidade serializada por método virtual (não um layout fixo nosso). Msgtypes de create nesta versão:
0x519/0x51a (na rakion-final o inbound é 0x307 via `HandleMessage`).

## RAIZ QUE AMARRA OS DOIS MUROS (conclusão do ataque)
Movimento (0x30a) do bot FUNCIONA porque só ATUALIZA uma entidade que já existe (o avatar 0x4b), e
passa pelo gate de endpoint (0x319). Já o CREATE (player OU npc) precisa **INSTANCIAR** — e a
instanciação exige o remetente ser um **PEER DE SESSÃO validado**, coisa que o **roteador de
mensagens do exe EMPACOTADO** (protegido por xldr) concede só a quem entrou pela via de sessão
(`AddPlayer`/`AddRemotePlayer`), cujo GATILHO mora nesse exe. O bot entrou pela via de FIELD (0x38/
0x4b) → não é peer de sessão → o create é roteado p/ o lixo antes de chegar ao `HandleMessage`.

**Portanto:** os dois muros são a MESMA raiz — *virar peer de sessão*. As duas saídas conhecidas:
1. **2º cliente real** (a sessão o adiciona nativamente) — vetado no projeto (offline 1-cliente).
2. **Reverter o exe empacotado** (xldr/packer) p/ achar o gatilho de `AddRemotePlayer` e forjá-lo —
   frente de RE nova (unpack do main exe), fora do escopo de hoje.

O que a engine desempacotada nos deu de graça: `AddPlayer@CSessionState`, `ChangeTeam@0x36109f00`,
`SetAsRemoteEntity@0x3600cfd0` são **exports** — se um dia houver um caminho de invocá-los no host
(sem injeção), o combatente sai deles. Enquanto isso, o teto server-side (anda/colide/dano/morte/
vitória) é o entregável sem furar o packer.

## VIRADA: o exe principal (`rakion.bin`) NÃO é empacotado — a recepção inteira é reversível
`rakion-final/Bin/rakion.bin` (== rakion.exe, **1.8MB, .text @0x401000 SEM ASLR, ImageBase 0x400000**)
está **DESEMPACOTADO** e importa da engine: **`AddRemotePlayer`** (IAT @0x4d01f4), **`HandleMessage`**
(IAT @0x4d03c8), `GetPlayer@CSessionState` (@0x4d03b4). ⇒ o "muro empacotado" era falso alarme p/ o
GATILHO — o exe do cliente é RE-ável estaticamente. (gamemp/entitiesmp seguem packed, mas o dispatch
de recepção que importa está no exe.)

### O dispatch de recepção (o roteador que barrava o 0x307)
`rakion.bin @0x412339` chama `HandleMessage@CSessionState` (o dispatcher 0x307–0x312 do create). O
caminho até lá:
```
0x412312: mov ecx, ebx              ; ebx = objeto de sessão/jogo
0x412314: call 0x40b8d0             ; GATE
0x412319: test eax,eax ; je skip    ; se gate==0, NÃO processa o create
0x412339: call HandleMessage        ; cria a entidade (0x307 -> AddRemoteGeneralNpc)
```
**O gate `0x40b8d0` é `return [obj+0x180] == 0x1d`** — uma checagem de **FASE de jogo** (estado 0x1d=29),
**NÃO** validação de peer. ⇒ o create do bot é aceito SE chegar quando `[sessão+0x180]==0x1d`. O
roteador maior é uma máquina de estados por `[+0x180]` (visto também `cmp [+0x180],0x17`) + categoria
de msg (`[esp+0x20]` vs 0xa). Descobrir o enum de estado (quando vale 0x1d) é o próximo passo — e é
100% estático agora.

### Reavaliação do 0x307 refutado
A refutação ([[0x307-npc-create-relay-refutado]]) provavelmente foi **fase errada** (state≠0x1d) ou seq
reliable — NÃO "entidade remota exige peer real". Com o exe aberto, o create-NPC volta a ser caminho
vivo: mandar 0x307 reliable (seq monotônica, endpoint 0x319) DURANTE a fase `[+0x180]==0x1d`.

## Estado dos muros (fim da sessão de ataque)
| muro | status | insumo |
|---|---|---|
| **1 — entrega reliable** | **QUEBRADO** | `IsApplyReliableUDP` 2 gates decodificados; satisfazível |
| **2 — trigger do combatente** | **ABERTO** (era "empacotado", é reversível) | `rakion.bin` unpacked; dispatch @0x412339; gate de FASE @0x40b8d0 (state 0x1d), não peer |
| **2b — serialização do create** | mapeado | create = entidade via vtable `[ent+0x1b8]`; NPC Read_t é stub (28B header); msgtype final 0x307 |

**Próximo passo estático (sem teste in-game):** mapear o enum de estado `[sessão+0x180]` no `rakion.bin`
(quem escreve 0x1d) → saber a fase exata p/ o create ser aceito → forjar o 0x307 nessa janela. Todo o
caminho de recepção agora é RE estática no exe desempacotado.

## FURO FINAL DO MURO 2 — todos os binários reversados (nenhum empacotado)
Descoberta que corrige o MODELO inteiro:

- **`gamemp.dll` e `entitiesmp.dll` do rakion-final NÃO estão empacotados** (seções normais, orig. 2007).
  Os três binários (rakion.bin, gamemp, entitiesmp) são RE estática. Não há packer a furar.
- **`AddRemotePlayer` é IMPORT MORTO.** Busca byte-a-byte no `rakion.bin`: `call [0x4d01f4]`=**0 hits**,
  `call thunk`=**0 hits**, ref-de-dado só nos 2 thunks `jmp`. ⇒ **o combatente NÃO nasce de
  `AddRemotePlayer`** — a premissa da sessão inteira estava errada. (idem rakion-new: sem caller.)
- **`gamemp` faz o TEAM-SETUP do combatente**, não a criação: importa `GetPlayer@CSessionState`
  (0x100261c4) + `ChangeTeam@CSessionState` (0x100261cc) + `ChangeTeam@CNetworkLibrary` (0x100261c0);
  call-sites @0x10011d93/0x10011e40. A função pega o player da **tabela de sessão `+0x1d20`**
  (`mov edx,[eax+esi*4+0x1d20]`) e seta o time (+0x26c). **Mas `gamemp` NÃO importa `AddPlayer`** → não
  cria a entrada; só modifica uma que já existe.

### Modelo corrigido (a RAIZ verdadeira)
As entradas de combatente na tabela `+0x1d20` são criadas **DENTRO da `engine.dll`**, no processamento
do **join de SESSÃO** (não exportado como uma mensagem única que a gente forje; nasce do fluxo de peer
da SE1). O `gamemp` depois só ajusta (team via `ChangeTeam`). O bot entra pela via de **FIELD**
(0x38/0x4b) — que popula a tabela de field, **não** a `+0x1d20` de sessão. Por isso:
- O humano vê o bot (avatar de field), colide (corpo), mas o **raycast de hit testa a `+0x1d20`** — onde
  o bot **não tem entrada de combatente** → `ReceiveDamage` nunca roda → zero HIT×N.
- `ChangeTeam` do bot não tem em quê agir (sem entrada `+0x1d20`).

**Conclusão do ataque (honesta):** o Muro 2 não é "código empacotado" (todos os bins abrem) nem
`AddRemotePlayer` (morto). É **arquitetural**: a entrada de combatente `+0x1d20` é criada pelo fluxo de
peer de SESSÃO interno da engine, que só dispara p/ um **peer de sessão real** (2º cliente). Num setup
offline 1-cliente, sem ser peer de sessão, não há como popular a `+0x1d20` do bot por mensagem — a não
ser o caminho **NPC** (tabela `+0x1d70`, `AddRemoteGeneralNpc`, 100% engine, dirigível), que é o
experimento deployado (0x8307+0x319). O combatente-PLAYER nativo exige peer de sessão real; ponto.

### Insumo de valor perene (RVAs no binário real, tudo reversável)
| item | onde | uso futuro |
|---|---|---|
| `IsApplyReliableUDP` 2 gates | engine 0x36109e20 | fazer QUALQUER reliable do bot ser aceito |
| gate de FASE do create | rakion.bin 0x40b8d0 (`[+0x180]==0x1d`) | janela p/ o 0x307 |
| dispatch do create | rakion.bin 0x412339 → `HandleMessage` | receber create no cliente |
| team-setup | gamemp 0x10011d93/e40 (`GetPlayer`+`ChangeTeam` na `+0x1d20`) | setar team do combatente |
| `AddRemoteGeneralNpc` | engine 0x361097a0 (caso 0x307) | criar NPC hittável (via NPC) |
| `AddPlayer@CSessionState` | engine export | criaria a entrada `+0x1d20` (só via engine-interno) |
