# RE Completo do Monstro "Cell" — Rakion (engine.dll/SE1 + entitiesmp.dll)

> **Finalidade.** Mapear, byte a byte, como um **monstro Cell** (carta de monstro que o jogador
> invoca na partida) **nasce, anda, ataca, recebe hit e morre** dentro do stage — para reaproveitar
> esse molde no **bot**. Reconstruído por RE de `engine.dll` (Serious Engine 1 open-source,
> ImageBase **0x36000000**, exports C++) e `entitiesmp.dll` (lógica de entidades do Rakion,
> ImageBase **0x35000000**, build `rakion-new/Bin` desempacotado), cruzado com a fonte aberta da
> SE1 (`Croteam-Official/Serious-Engine`). Complementa [`pvp-stage-re.md`](pvp-stage-re.md).

> **O que é "Cell".** Não existe classe `CCell`. Em Rakion, *Cell* é a **carta de monstro** que o
> jogador invoca; cada Cell instancia uma classe `CNpc*` derivada de **`CNpcBase` (id 0x044d)**.
> Exemplos (id de classe): `CNpcGolem1`=0x468, `CNpcMasterGolem`=0x465, `CNpcCrossBow1`=0x46c,
> `CNpcDragon1`=0x463, `CNpcPanzer1`=0x467, `CNpcTaurus1`=0x46a, `CNpcAngelKnight1`=0x460,
> `CNpcSuccubus1`=0x455, `CNpcBlazer1`=0x461, `CNpcNak1`=0x466. São ~250 classes `CNpc*` (níveis
> 1–4, bases, reis). Todas compartilham a base `CNpcBase`, então **o molde de comportamento é um só**.

---

## 0. Resumo executivo (o que isto destrava para o bot)

1. **Render**: o Cell remoto é criado pelo opcode **`0x307`** → `AddRemoteGeneralNpc` →
   `CWorld::CreateNpc` → entidade `CNpc*` REAL no mundo. **Mas o 0x307 vindo do nosso servidor
   (não-peer) não renderizou in-game** (Grunt e Golem testados). Duas causas candidatas, ambas
   testáveis: **(a)** usei `owner = faction (0x14=20)`, mas o `owner` é **índice de player-slot**
   e indexa `owner*9` numa tabela sem bounds-check → estoura; **(b)** o datagrama relayado de
   não-peer pode ser barrado antes do dispatch.
2. **Movimento**: um Cell **remoto** (não-dono) é **dirigido pela rede** via **`0x30f` type=2**
   (chave `owner*9+sub`), não roda IA local. Quem roda a IA completa (`Active→AttackEnemy→
   PerformAttack`) é o **cliente dono**. ⇒ nosso servidor pode ser o "dono" e dirigir o Cell por
   `0x30f` (server-side AI), exatamente como o BotAi já faz.
3. **Hit / "HIT × N"**: o contador de combo (`AddHitCount`) é **LOCAL** e roda no cliente do
   **atacante**, incrementando um campo **no próprio objeto do atacante** (`+0xb44`), **só quando o
   atacante é o jogador local**. ⇒ para o humano ver "HIT × N" no bot, a `ReceiveDamage` do bot
   precisa **rodar como entidade real simulada no cliente do humano** — nenhum pacote custom de
   contador é necessário; o counter é automático assim que o bot é uma entidade hittável (viva +
   team inimigo + HP).

**Conclusão estrutural** (igual à do type-7): render + movimento nativo + HIT×N **exigem** o bot
como entidade real no cliente do humano — via `0x307` corrigido (caminho simples, **não confirmado**)
ou via peer SE1 real (mini-peer/headless). O HIT×N em si **não** é o gargalo; ele "vem de graça".

---

## 1. Modelo de classe da `CNpcBase` (0x044d)

Lido do export `CNpcBase_DLLClass @ 0x357bb3d8` (struct `CDLLEntityClass` **estendida do Rakion**:
o Rakion inseriu um par `(array,count)` em +0x18/+0x1c, deslocando +8 vs SE1 stock):

| campo | offset | valor (CNpcBase) | nota |
|---|---|---|---|
| `dec_aepProperties` / ct | +0x00 / +0x04 | `0x357ba948` / 56 | array de descritores de propriedade |
| `dec_aeheHandlers?` / ct | +0x08 / +0x0c | `0x357e8a08` / 125 | **cache runtime (zerado em static-init)** |
| **`dec_aeheHandlers`** / ct | **+0x10 / +0x14** | `0x357baa28` / **155** | **máquina de estados** (ver §1.1) |
| `<extra Rakion>` / ct | +0x18 / +0x1c | `0x357e99a8` / 26 | cache runtime (zerado) |
| `dec_strName` | +0x20 | `0x355f6b10` | "CNpcBase" |
| `dec_strIcon` | +0x24 | `0x355ed380` | "" (string vazia compartilhada) |
| **`dec_iID`** | **+0x28** | **0x044d** | id de classe |
| `dec_pdecBase` | +0x2c | 0 (base em engine.dll) | classe base |
| **`dec_New`** | **+0x30** | **0x351e52a0** | construtor de instância |
| `dec_OnInitClass` | +0x34 | 0x351be640 | registra/precache |

**Instância** (de `CNpcBase::New @ 0x351e52a0`):
- `operator new(0x3ad8)` → objeto de **15064 bytes**.
- ctor **`0x351e43c0`**: `mov [esi], 0x35618334` ⇒ **vtable = `0x35618334`**; chama o ctor base
  (engine.dll, `[0x355ea088]`) e constrói membros `CTString` em +0x37c/+0x380.

### 1.1 Máquina de estados (handlers)
O array em **+0x10** tem **155 entradas** de 16 bytes: `[stateCode][baseState][fn][name]`, onde
`stateCode = (classId<<16)|stateLocal` (ex.: `0x044d0038`), `baseState=0xFFFFFFFF` na raiz, `fn` é o
handler. **Os `name` estão stripados** (todos apontam pro "" em `0x355ed380`), então temos os 155
handlers (estado + função) mas **sem nomes simbólicos** — reconstruir cada um às cegas é inviável; o
molde de alto nível vem da fonte SE1 (§3) e as funções-chave estão cravadas abaixo.

---

## 2. Spawn / criação do Cell na partida

### 2.1 Cadeia de criação (engine.dll)
```
opcode 0x307 (dispatch @0x3610d80a..0x3610d8ef)
  └─ AddRemoteGeneralNpc  @0x361097a0
       ├─ slot = world + 0x1d70 + (owner*9 + sub)*4      ; tabela de handles do NPC remoto
       ├─ se slot != -1 → FindEntity(0x360c3530); já existe → retorna
       ├─ aloca id novo  = [0x362ba778]+0x1304 (++)        ; contador global de entidade
       ├─ CWorld::CreateNpc  @0x360c5c50
       │     ├─ resolve classId → CDLLEntityClass  (0x360e2f80)   ; classe não registrada → NULL → nada
       │     └─ CreateEntity (0x360c4830) → dec_New → entidade no mundo
       └─ 0x3619e1e0  (setup/placement do NPC remoto: owner, classId, posição)
```

### 2.2 Formato wire do 0x307 (28B header + blob)
O dispatch lê do stream da mensagem (`ReadBytes(4)` = `0x36100cf0`, `ReadBlob` = `0x36100e00`):
```
[u8  owner]      ; índice de PLAYER-SLOT do invocador (NÃO faction!)  ← ver §2.3
[u8  sub]        ; sub-slot do NPC (0..8) — até 9 NPCs por owner
[u16 classId]    ; 0x468 Golem, 0x46c CrossBow, ...
[6×f32 placement]; pos (x,y,z) + ângulos (h,p,b)
[blob]           ; estado da entidade (Read_t)
```
Depois de criar, relê `owner`/`sub` e busca a entidade recém-criada em
`world+0x1d70+(owner*9+sub)*4` para continuar o setup.

### 2.3 ⚠️ `owner` ≠ team — e o team não vem do owner (correção cravada)
O setup `0x3619e1e0(entity, owner, sub)` faz **só**:
```
mov byte [entity+0x264], owner     ; id do dono (chave da tabela)
mov byte [entity+0x265], sub
ret 8
```
⇒ **`owner` vai para +0x264, `sub` para +0x265 — NÃO para o team (+0x26c).** Logo:
- A crença do código atual ("o byte owner = o team byte de +0x26c") está **ERRADA**. Setar
  `NpcOwner = faction` põe a faction em +0x264 (id do dono), **não** no team de combate.
- **O team (+0x26c) é setado pela CLASSE**, não pelo nosso byte: há escritas constantes
  `mov byte [esi+0x26c], 0x14` em `0x351dacc9` (faixa da CNpcBase → **default BLUE**) e outras que
  leem o team de registrador (do blob/Read_t). ⇒ um Cell nasce com o team da sua classe/blob; se
  isso não casar com "inimigo do humano", o `IsEnemy` falha e **não há HIT×N mesmo renderizando**.
- **Bounds:** a tabela `world+0x1d70` vai até a tabela v2 em `+0x2048` ⇒ ~182 entradas = **~20
  owners × 9 subs**. `owner=faction RED(0x0a=10)` → idx 90..98 (ok); `owner=faction BLUE(0x14=20)`
  → idx 180..188, **estoura** em sub≥2 para dentro de `+0x2048`. ⇒ bots BLUE com ≥2 NPCs corrompem
  a tabela v2.

**Implicação:** o `owner` só precisa ser um id consistente entre create(0x307) e move(0x30f) e
**dentro de [0,19]**; o que governa o HIT×N é o **team em +0x26c**, que precisa ser setado para a
faction inimiga do humano — via blob/Read_t ou patch pós-create, **não** via o byte owner.

### 2.3b Formato REAL do receptor (a verdade definitiva) — dispatch @0x3610d80a
O receptor é a autoridade do wire. O dispatch do 0x307 lê via `Read(buf,size)` @0x36100cf0 (cópia
**byte-alinhada**; `CNetMessage::operator<<` também é byte-alinhado — `op<<(u8)`=Write(ptr,1),
`op<<(f32)`=Write(ptr,4) — **nada de bit-packing**):

```
Read 1  -> owner (=team, +0x26c)     |
Read 1  -> sub                       |  header FIXO de 28B
Read 2  -> classId (u16)             |  (sub é SEMPRE lido — não-condicional)
Read 4×6-> placement (pos xyz+hpb)   |
ReadBlob(&msg) -> o RESTO num CNetMessage           ; o "blob" da entidade
AddRemoteGeneralNpc(owner,sub,classId) -> cria a entidade
entity->vtable[+0x118](&msg)         ; consome o blob (método BASE da engine; ver nota)
CEntity::Initialize(...)             ; inicializa (modelo/colisão/flags/render)
```

**Confirmado:** o header de 28B `[u8 owner][u8 sub][u16 classId][6×f32]` que o `EncodeCreateNpcBody`
atual escreve **está correto** (e `sub` é sempre necessário). O `GetCreateInfo`@0x351c8480 (vtable[1],
com `sub` condicional ao estado) é o sender de **outro caminho** (entity-message geral/boss), **não**
do 0x307 — descartado para este uso.

⚠️ **Blob = serialização da entidade pela BASE da engine** (`vtable+0x118` resolve para um thunk de
import da `engine.dll`; a identificação exata do método/offset do vtable ficou **imprecisa** — slot
caiu em `ReceiveHoldAttack`, então o offset 0x118 ou a base do vtable precisam de conferência). O
blob de 50B atual é **chutado** → "unknown error"/meio-init → sem modelo/render. **Reconstruir esse
blob às cegas é frágil**; o jeito sólido (e a convenção do projeto) é **capturar um create real e
casar byte-a-byte** (golden), pois é a serialização base da engine — complexa. A MESMA serialização
vale para o caminho peer. **`owner==team`** ⇒ §2.3 corrigida.

### 2.3c Serializador COMPLETO do create — `GetCreateInfo` + `GetStatusInfo` (decode estático)
Revisão: o `GetCreateInfo`@0x351c8480 **É** o sender do 0x307 (o header 28B = sua primeira parte; o
`ReadBlob` do dispatch captura o resto). `CNetMessage::operator<<` é **byte-alinhado**
(`op<<(u8)`=Write(ptr,1), etc.). Sequência completa (origem = offset no `this`):

**Header (28B)** — já correto no código:
`u8 team(+0x26c)` · `u8 sub(+0x26d)` · `u16 classId` · `f32×6 placement(GetPlacement)`

**Blob — prefixo:** `u8 typeId(+0x3920)` · `s32 (+0x648)` · `u8 (+0x898)` · `s32 (+0x7d0)`

**Blob — status aninhado (`GetStatusInfo`@0x351c87f0, 26 writes, concatenado SEM length-prefix):**
`f32 HP(getter +0x270)` · `f32 (+0x3a04)` · `u8 (+0x7d0)` · `s32 (+0x84c)` · **`CTString nome(+0x380)`**
(u8 len + chars) · `u8 team(+0x26c)` · `3×u8 classinfo(GetEntityClassInfo)` · `u8 (+0x808)` ·
`u8 (+0x818)` · `u8 (+0x3a60)` · `u8 (+0x3a64)` · `u32 (+0x3a68)` · `u8 (computado)` · `s32 (+0x7dc)` ·
`s32 (+0x648)` · `s32 (+0x888)` · `s32 (+0x898)` · `u8/CTString? (+0x3aa6)` · `u8 (+0x3aaa)` ·
`u8 (+0x3aac)` · `s32 (computado)` · `u8 (computado)` — ret @0x351c8b68.

**Estado do decode:** estrutura/tipos/offsets **cravados**. **Ambiguidades remanescentes** (não
resolvíveis com segurança só por RE estática): (1) `+0x3aa6` é CTString (len+chars) ou bytes soltos?;
(2) os ~3 campos "computado"; (3) os **VALORES** corretos de ~18 campos de estado interno para um NPC
*fresh* (HP=max e nome são conhecidos; a maioria do resto deve ser 0, **não confirmado**). Os
`classinfo` (3×u8) e os stats `+0x3axx` podem ser class-específicos.

⇒ **Para fechar byte-exato sem chute, a via sólida é UMA captura de um Cell sendo invocado** (golden,
convenção do projeto): casa os ~30 campos de uma vez e remove toda ambiguidade do tail. Mesma
serialização vale para o caminho peer. Sem captura, resta reescrever best-effort (HP/nome/team
conhecidos, resto=0) e **iterar via teste in-game** — o que conflita com "RE completa antes de testar".

### 2.4 Por que o 0x307 do servidor não renderizou (estado atual)
In-game, `0x307` com Grunt (0x157) e Golem (0x468) — 84B relayados — **não apareceu**. Fatos:
- **Grunt (CGrunt 0x157)**: classe da SeriousSam, **não registrada** no runtime do Rakion → o
  resolver `0x360e2f80` devolve inválido → `CreateNpc` não cria. (Esperado.)
- **Golem (0x468)**: classe **registrada** (está no entitiesmp) → `CreateNpc` deveria criar. Não
  renderizou mesmo assim ⇒ o entrave **não é registro**; é (a) o `owner=faction` estourando a
  tabela §2.3 e/ou (b) o datagrama relayado de **não-peer** ser barrado no dispatch (mesmo muro do
  type-7, "+0x1d8 gate reliable"). **Pendente retestar com `owner` = player-slot.**

### 2.5 Caminho de render "garantido" (referência)
Cells/monstros que renderizam nativo são **entidades reais do mundo**, criadas por um de dois meios,
ambos passando pela replicação de entidade da SE1 (canal reliable de sessão) — não por datagrama
custom:
- **Level-placed**: golem dourado/master golems do Golem War vêm do `.wld` (ver
  [`golem-war-map-geometry`]) — cada cliente carrega o mesmo mundo, logo renderizam em todos.
- **Replicação de peer**: um peer real cria a entidade e a engine replica via evento de criação
  (mesmo canal do ADDPLAYER). É o que o mini-peer/headless precisa fechar.

---

## 3. Movimento

### 3.1 Modelo SE1 (molde herdado — fonte aberta)
- A IA seta **velocidade/rotação desejada relativa**: `SetDesiredTranslation(FLOAT3D)` →
  `en_vDesiredTranslationRelative`; `SetDesiredRotation(ANGLE3D)` → `en_aDesiredRotationRelative`
  (`MovableEntity.es`).
- Por tick (`PreMoving/DoMoving/PostMoving`) a engine converte para **absoluto** e integra com
  aceleração/gravidade/forças: `en_vCurrentTranslationAbsolute`, `en_aCurrentRotationAbsolute`
  (× `fTickQuantum`), testa colisão e aplica resposta (slide/bounce por `EPF_ONBLOCK_*`).
- **Máquina de IA (`CEnemyBase`)**: `Active()` → decide perseguir/atacar pelo `CalcDist(m_penEnemy)`
  → `AttackEnemy()` → `InitializeAttack`/`PerformAttack`/`StopAttack`. `PerformAttack` roda um loop a
  cada `m_fMoveFrequency` ticks: recalcula `m_vDesiredPosition`, chama `SetDesiredMovement()`
  (devolve flags de animação), agenda disparo em `m_fShootTime`. Distâncias: `m_fCloseDistance`
  (melee), `m_fAttackDistance` (range), `m_fStopDistance`, `m_fIgnoreRange`; velocidades
  `m_fCloseRunSpeed`/`m_fAttackRunSpeed` + rotações. Alvo em `m_penEnemy`
  (`m_ttTarget`=SOFT/HARD).

### 3.2 Quem roda a IA na partida P2P
- O `worldserv` **não roda a engine** (é socket/relay/MySQL — ver [`worldserv-nao-roda-engine-se1`]).
  Logo a IA/simulação 3D do Cell é **client-side**: roda no **cliente dono** (o invocador).
- **Clientes não-donos** recebem o Cell como **NPC remoto** e aplicam **`0x30f` type=2** (chave
  `owner*9+sub`, a MESMA tabela `world+0x1d70` do 0x307) para atualizar posição — **eles renderizam
  a posição relayada, não rodam IA**.
- ⇒ Para o bot: **o servidor pode ser o "dono"** e dirigir o Cell por `0x30f` (server-side AI). É o
  modelo que o `BotAi` + `UdpGameplay` já implementam para o movimento do bot.

---

## 4. Ataque

- Em `PerformAttack`, ao chegar `m_fShootTime`, a subclasse executa o `AttackTarget()` virtual
  (cada Cell define o seu), que chama **`InflictDirectDamage(penTarget, this, dmtType, fDamage,
  vHitPoint, vDirection)`** (`Entity.h`).
- Validação de acerto: `CanAttackEnemy()` — checa **alcance** + **cone** (`cos(ângulo) ≥ cos(45°)`).
- `dmtType` (DamageType) e `fDamage` são ajustados por `DamageStrength()` (resistência por tipo de
  entidade) e pelo multiplicador de dificuldade.

---

## 5. Receber dano — o caminho do HIT (objetivo #1)

### 5.1 `ReceiveDamage` da vítima  @0x3518ce40
`ReceiveDamage(this=vítima, penInflictor, dmtType, fDamage, vHitPoint, vDirection)`. Gates, em ordem:

| checagem | offset / fn | significado |
|---|---|---|
| `IsFlagOn(ENF_ALIVE=8)` | `en_ulFlags`+0x10, fn `0x351c01f0`→`[0x355ea650]` | vítima viva |
| `[this+0x638] != 0` | +0x638 | morrendo/spawn-protect → caminho alternativo, sai |
| `[this+0x664] == 1` | +0x664 | imune/template → sai |
| `[this+0x394] != 0` | +0x394 | ativo (senão pula bloco de hit-count) |
| validação do inflictor | `0x3518a2b0(penInflictor)` | inflictor válido |
| **IsEnemy** | `0x35421280(teamVítima, teamInflictor, inflictor)` | **team** da vítima @+0x26c × team do atacante |

- **Team** mora no byte **+0x26c** (RED=0x0a, BLUE=0x14; 0/neutro **falha** o IsEnemy). Sub-id em
  +0x26d. **HP** em **+0x624** (`fldz; fcomp [esi+0x624]`: HP≤0 → já morto, sai).

### 5.2 "HIT × N" — `AddHitCount`  @0x3533ad30
Chamado **no inflictor (atacante)**, não na vítima:
```
0x3518cefc: cmp edi, eax        ; edi = penInflictor ; eax = jogador LOCAL (0x354f9e34→[0x355ea6f4])
0x3518cf00: push 0xa            ; dmtType de combo
0x3518cf02: mov ecx, edi        ; this = ATACANTE
0x3518cf04: call 0x3533ad30     ; AddHitCount
```
⇒ **só incrementa se o atacante for o jogador local.** O combo vive **no objeto do atacante**:

| campo | offset | papel |
|---|---|---|
| último hit (tempo) | **+0xb40** | timestamp do hit anterior |
| **contador do combo** | **+0xb44** | **o N do "HIT × N"** (`inc [esi+0xb44]`) |
| grupo do tipo de dano | +0xb48 | 0xa para tipos {0xa,0xb,0xe}; senão 1 |
| valor do 1º hit | +0xb4c | gravado quando count==1 |

Lógica: bucketiza `dmtType` (`0xa/0xb/0xe → 0xa`, resto → 1); se o grupo mudou, reseta o combo; se
`(agora − últimoHit) ≤ janela` (constante double @`0x355f4630`), **incrementa +0xb44** e atualiza
+0xb40; fora da janela, reseta. **Tudo local, sem envio de rede.**

### 5.3 Consequência para o bot
O "HIT × N" é **automático e local**: aparece quando a `ReceiveDamage` do alvo roda no cliente do
atacante com `inflictor == jogador local`. Para o humano ver HIT×N batendo no bot, o bot precisa ser
**entidade real simulada no cliente do humano**, com `ENF_ALIVE` setado, **team inimigo** em +0x26c
(0x0a/0x14, nunca 0) e **HP>0** em +0x624. Satisfeito isso, o counter "vem de graça" — não há pacote
de contador a sintetizar.

---

## 6. Morte
Quando `HP (+0x624) ≤ 0`, o fluxo de dano para de creditar e a IA dispara o evento de morte (`Die`
no molde `CEnemyBase`: animação + som + `ENF_ALIVE` limpo + remoção/limpeza). No PvP do Rakion o
combate é **cliente-autoritativo** (a vítima reporta a própria morte 0x4f — ver
[`pvp-stage-re.md`](pvp-stage-re.md) §0); para um Cell dirigido pelo servidor, a "morte" é decidida
por quem roda a IA (o dono) e propagada como estado.

---

## 7. Endereços (referência rápida)

**engine.dll (0x36000000)**
| símbolo | endereço |
|---|---|
| dispatch 0x307 | 0x3610d80a |
| `AddRemoteGeneralNpc` | 0x361097a0 |
| `AddRemoteNpc_v2` | 0x36109850 |
| `CWorld::CreateNpc` | 0x360c5c50 |
| resolver classId→class | 0x360e2f80 |
| `CreateEntity` | 0x360c4830 |
| setup NPC remoto | 0x3619e1e0 |
| stream `ReadBytes(4)` / `ReadBlob` | 0x36100cf0 / 0x36100e00 |
| tabela handles NPC remoto | `world + 0x1d70` (índice `owner*9+sub`) |

**entitiesmp.dll (0x35000000)**
| símbolo | endereço |
|---|---|
| `CNpcBase::New` (size 0x3ad8) | 0x351e52a0 |
| `CNpcBase::ctor` | 0x351e43c0 |
| **vtable `CNpcBase`** | **0x35618334** |
| `IsFlagOn(8)`/IsAlive | 0x351c01f0 |
| `ReceiveDamage` (vítima) | 0x3518ce40 |
| `IsEnemy(team×team)` | 0x35421280 |
| validação do inflictor | 0x3518a2b0 |
| **`AddHitCount` (HIT×N)** | **0x3533ad30** |
| jogador local | 0x354f9e34 → `[0x355ea6f4]` |

**Layout da instância `CNpc*`**
| offset | campo |
|---|---|
| +0x10 | `en_ulFlags` (ENF_ALIVE = bit 0x8) |
| +0x100 | estado (usado em registro de team-table) |
| +0x26c / +0x26d | **team** / sub-id |
| +0x394 | ativo |
| +0x624 | **HP** |
| +0x628 / +0x638 / +0x664 | flag / morrendo / imune-template |
| +0xb40 / +0xb44 / +0xb48 / +0xb4c | combo: tempo / **contador HIT×N** / grupo / 1º valor |
| +0x3a3c | (setter/getter `0x351c0200`/`0x351c0210`) |

---

## 8. Próximos passos sugeridos (decorrentes deste RE)
1. **Retestar 0x307 com `owner` = player-slot** (não faction) — correção §2.3; é o caminho mais
   barato para render nativo. Se renderizar, o bot-como-NPC fica dirigível por `0x30f` server-side
   (movimento §3.2) e o HIT×N vem automático (§5.3).
2. Se o muro de não-peer §2.4(b) persistir mesmo com owner correto, o render nativo só fecha pelo
   **peer SE1 real** (mini-peer/headless) — ver [`headless-engine-host`] / [`bot-hittability-type7-verdict`].
3. Para o combate humano→bot, garantir que a entidade do bot no cliente do humano tenha
   `ENF_ALIVE` + team inimigo (+0x26c) + HP>0 (+0x624) — sem isso o IsEnemy/IsValidReceiveDamage
   barra e o HIT×N nem dispara.
