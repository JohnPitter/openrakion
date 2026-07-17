# Engenharia reversa de cells, criaturas e NPCs — Rakion v258

## Escopo e veredito

Este documento cobre catálogo de criaturas/cells, Cell Points, summon, entidades NPC de stage,
Master/Gold Golem, movimento, eventos e recompensas. PvP e progressão dos stages estão nos
documentos dedicados.

**Veredito:** o cliente entregue configura 47 tipos de criatura, mas somente 43 possuem manifest
e classe carregável em `Classes.xfs`/`entitiesmp.dll`. As quatro entradas `NpcBlackDragon*` não
existem como classe nesta build. O runtime das 43 classes presentes está no cliente,
mas o World .NET não simula criaturas. Em stage solo, NPCs são criados e executados localmente.
Em sessão multi-cliente, o fallback do World agora valida e relaya os envelopes reliable
`0x8307/08/09/0B/0C/10/12` somente entre peers autenticados do mesmo field; ele não cria o
snapshot, porque no desenho original o master do cliente o monta e o direciona ao novo peer.
O World também não interpreta o init blob durante o relay; existe, porém, um decoder offline
estrito para as três famílias. `npcinfo` fornece a curva de EXP usada na progressão
transacional das cells equipadas pelo `0x50`; gold por kill continua sem consumidor server-side.

## Fontes

- `DataSetup.xfs` do cliente entregue: `creatures.dat` com 387.750 bytes e
  `creaturelist.txt` com 47 caminhos `.ecl`;
- `engine.dll`: `CSessionState::HandleMessage @ 0x3610D7C0`;
- RE em `stage_spawn_re*.txt` e `engine_spawn.txt`;
- SQL legado `npcinfo` e `iteminfo`;
- implementação atual em `server/RakionServer`.

`creatures.dat` não foi editado. `tools/extract_cell_catalog.py --data-setup-xfs` lê diretamente
o XFS ativo e delimita 47 blocos de 8.118 bytes, na ordem de `creaturelist.txt`, mais 6.204 bytes
finais. O tamanho fecha exatamente `47 × 8.118 + 47 × 4 × 33`.

O layout completo das 24 séries por nível, os dez labels do painel e as curvas de
Attack/Energy/CP estão em [`npc-stat-curves.md`](npc-stat-curves.md).

O loader `ReadNpcDataFromFile → 0x35228D10` confirma 99 níveis por tipo, registro runtime de
160 bytes e leitores escalares de 1, 2 e 4 bytes. A série de 99 `uint32` em `+0x18C` é o limiar
cumulativo de EXP da cell: `FUN_00454BD0` calcula a barra do nível atual subtraindo o limiar do
nível anterior. A série `uint16` em `+0x166E` fornece o ganho de CP pela morte de um NPC,
`uint16 +0x1734` fornece o custo CP de summon por nível e `float32 +0x18C0` alimenta o custo GOLD
de upgrade. SHA-256 da fonte
ativa: `items.dat=57cbab82c3eaf2ff7789a674d8e60121f0ccf5923d0da8f37fbedc86a0297372`,
`creaturelist.txt=5e21b26c9493b59244a9876c335d3de7c7be0c5a8767845f38c40b24b838e59e` e
`creatures.dat=de97e9aa6fa47792fe9de38770d77b55bb6bc308db9444d8a314d6681d554e3f`.

O snapshot solto de `ragezone/DataSetup` é de outra revisão: possui 51 entradas e 420.750 bytes.
Ele não é a golden source desta build e não deve ser usado para afirmar suporte às quatro classes
extras.

## Catálogo do cliente

`creaturelist.txt` lista dez famílias base:

| Índice base | Classe |
|---:|---|
| 0 | `NpcNak.ecl` |
| 1 | `NpcPanzer.ecl` |
| 2 | `NpcCrossBow.ecl` |
| 3 | `NpcBlazer.ecl` |
| 4 | `NpcGolem.ecl` |
| 5 | `NpcSoulCannon.ecl` |
| 6 | `NpcLongBow.ecl` |
| 7 | `NpcTaurus.ecl` |
| 8 | `NpcIceWind.ecl` |
| 9 | `NpcDragon.ecl` |

Também contém:

- `NpcMasterGolem` e `NpcGoldGolem`;
- variantes `2`, `3` e `4` das dez famílias;
- caminhos `NpcBlackDragon` e variantes `2..4`, sem manifest/classe nesta build;
- `NpcChocolateCake`.

São 47 entradas configuradas, ignorando linhas vazias e comentários, das quais 43 são carregáveis.
`GetCellType` retorna
`itemId-8000`, e `FUN_0040B940` usa o mesmo valor como tipo `npc` da curva. O `items.dat` ativo
contém os itens base e todas as variantes habilitadas no SQL, mas não contém um registro para
cada `.ecl` de asset; por isso o vínculo completo só é confirmado para o subconjunto equipável.

## Conteúdo de stage extraído

`DataSetup/LevelData` contém `stage_001.txt` até `Stage_055.txt`. O parser reproduzível
`tools/extract_stage_catalog.py` encontrou:

- 55 assets de stage, embora o SQL habilite apenas IDs `1..48`;
- nos 48 ativos: 1.210 definições `NpcSpawn` e 3.407 nomes de instância;
- classes ativas: Nak, Panzer, CrossBow, Blazer, Golem, SoulCannon, LongBow, Taurus, Dragon,
  BloodNak, SkyBlazer, IronGolem e o typo legado `AssultPanzer`;
- Stages `49..55`: classes Black/IceWind adicionais e rewards muito acima dos limites ativos,
  reforçando que não devem ser habilitados só porque o asset existe.

O contador representa instâncias declaradas em `npcname`, não necessariamente spawns simultâneos:
triggers podem reutilizar definições ou nunca alcançá-las. O parser também preserva level,
friendly/target, goal, tempo e rankvars, mas ainda não executa a máquina de Switch/Trigger.

## Mapeamento item, classe e tipo NPC

O parser de `items.dat` usa o marcador estrutural `u32 id, u16 0, u32 id`, seguido por nome,
cor, modelo e campos fixos. O resultado relevante para as classes vistas em `LevelData` é:

| Tipo NPC / índice | Item | Nome em `items.dat` | Classe `.ecl` | Alias `LevelData` | No SQL |
|---:|---:|---|---|---|:---:|
| 0 | 8000 | Nak | `NpcNak` | `nak` | sim |
| 1 | 8001 | Panzer | `NpcPanzer` | `panzer` | sim |
| 2 | 8002 | Crossbow | `NpcCrossBow` | `crossbow` | sim |
| 3 | 8003 | Blazer | `NpcBlazer` | `blazer` | sim |
| 4 | 8004 | Golem | `NpcGolem` | `golem` | sim |
| 5 | 8005 | SoulCannon | `NpcSoulCannon` | `soulcannon` | sim |
| 6 | 8006 | Longbow | `NpcLongBow` | `longbow` | sim |
| 7 | 8007 | Taurus | `NpcTaurus` | `taurus` | sim |
| 8 | 8008 | IceWind | `NpcIceWind` | `icewind` | sim |
| 9 | 8009 | Dragon | `NpcDragon` | `dragon` | sim |
| 10 | 8010 | Etc | `NpcMasterGolem` | — | sim |
| 12 | 8012 | ausente | `NpcNak2` | `blacknak` | não |
| 13 | 8013 | BloodNak | `NpcNak3` | `bloodnak` | sim |
| 15 | 8015 | BlackPenzer | `NpcPanzer2` | `blackpanzer` | sim |
| 16 | 8016 | AssaultPanzer | `NpcPanzer3` | `assaultpanzer`, `assultpanzer` | sim |
| 18 | 8018 | BlackCrossbow | `NpcCrossBow2` | `blackcrossbow` | sim |
| 21 | 8021 | BlackBlazer | `NpcBlazer2` | `blackblazer` | sim |
| 22 | 8022 | SkyBlazer | `NpcBlazer3` | `skyblazer` | sim |
| 25 | 8025 | IronGolem | `NpcGolem3` | `irongolem` | sim |
| 33 | 8033 | BlackTaurus | `NpcTaurus2` | `blacktaurus` | sim |
| 36 | 8036 | ausente | `NpcIceWind2` | `blackicewind` | não |
| 39 | 8039 | ausente | `NpcDragon2` | — | não |
| 42 | 8042 | ausente | `NpcBlackDragon` | `blackdragon` | não |

Os aliases foram comparados depois de normalizar apenas pontuação e os typos legados
`Penzer/Panzer` e `Assult/Assault`. As 14 classes usadas nos stages SQL ativos têm item e `.ecl`.
Nos assets inativos `49..55`, `blacknak` e `blackicewind` possuem classes `.ecl`; `blackdragon`
tem somente o caminho configurado, sem entrada em `Classes.xfs`. Nenhum dos três possui registro
correspondente no `items.dat` ativo. Logo esses aliases não devem ser
promovidos a conteúdo equipável sem uma decisão explícita de migração de dados.

`characterinfo.maxcp` é carregado como “Max cell point”; `chit` é documentado como “Cell
destruction”. No runtime, `CPlayer+0xB8C` guarda Max CP e `CPlayer+0x2714` guarda CP atual;
`SetCP` limita o valor ao intervalo `0..max`, e `CPlayer::Death` reduz exatamente 10% do CP atual
(`0x3DCCCCCD`). `AddCP` é chamado por dano e morte. `GetCP/AddCP/ReduceCP/GetMaxCP` são registrados
no runtime Lua por `FUN_0045FDD0`; isso não significa que todo débito passe por `ReduceCP`.

`DumpCpFieldReferences.py` fez uma varredura integral das instruções do dump runtime e encontrou 22
acessos diretos ao campo `+0x2714/+0xB8C` ou às seis rotinas de CP. Fora dos próprios accessors,
os calls nativos aparecem em receive-damage, death, respawn e um bloco ainda sem função tipada;
nenhum timer/update nativo escreve CP diretamente. A origem inicial também está fechada: o
construtor `CPlayer @ 0x35158DB0` grava zero em
`this[0x9C5]`, exatamente `+0x2714`; `GetInitData @ 0x35141000` serializa esse `float32`, e
`ApplyInitData @ 0x3515A0E0` o restaura no player remoto. `Respawn @ 0x35162370` recalcula Max CP,
mas não preenche o CP atual.

Para cobrir a chamada indireta, `tools/extract_script_api_usage.py` varreu as 130 entradas do
`Scripts.xfs` entregue. Somente `scripts\item\12070.lua` usa a API de CP: compara `GetCP()` com
`GetMaxCP()` e chama `AddCP(GetMaxCP() * 0,3)`. Não existe `ReduceCP`, `SetCP` ou outro `AddCP` no
corpus Lua, portanto esta build não possui regeneração passiva de CP por script/reflection. A
varredura nativa é reproduzida por `DumpCpFieldReferences.py`; o binding, construtor e serializers
são reproduzidos por `DecompileClientCellRuntime.py`.

Em `FUN_004525F0`, o texto 769, “CP consumption”, mostra `GetItemInfo+0xB8`. Isso confirma um
atributo agregado/modificador com esse rótulo, não o custo bruto de uma criatura.

O custo bruto foi localizado separadamente. O loader lê a série `uint16` de 99 níveis em
`creatures.dat+0x1734` para o campo runtime `NpcSetup[level]+0x70`. `FUN_351DBFF0` percorre os três
slots de cell equipados, resolve `itemId-8000`, lê esse campo no tipo/nível atual e calcula o custo
efetivo como `max(1, custoBase - modificadorPercentual * 0,01 * custoBase)`. Portanto essa série é
o **custo CP de summon por criatura e nível**. `tools/extract_cell_catalog.py` agora exporta a curva
como `summon_cp_cost`.

O caminho de intenção também está fechado no dump runtime de `entitiesmp.dll`. `FUN_35148F20`
entrega os bits de input e a estrutura em `CPlayer+0x26B0` a `FUN_351DBF00`. A estrutura possui
um contador em `+0x00`, seguido por três registros de `0x20` bytes a partir de `CPlayer+0x26B4`;
o CP atual fica em `base+0x64`, isto é, `CPlayer+0x2714`.
Os bits `0x200/0x400/0x800` escolhem os slots `0/1/2`. Cada registro mantém:

- índice do slot em `+0x00`;
- identificador/propriedade de estado em `+0x0C`;
- custo efetivo `float32` em `+0x14`;
- stride total de `0x20` bytes.

`FUN_351DBDF0` promove estado `0 → 1` quando `CP atual >= custo`; `FUN_351DBE40` rebaixa
`1 → 0` quando o saldo fica insuficiente. `FUN_351DBE90` aceita somente um slot em estado `1` e
faz o débito diretamente com `CP atual -= custo efetivo`. Em seguida `FUN_351DBF00` publica estado
`2` e devolve o slot para `FUN_350E3260`, que cria a entidade e envia o reliable `0x0307`.
Portanto o summon não chama `ReduceCP`: a subtração direta é o comportamento original desta build.

`FUN_35144220` confirma que estado `2` significa slot com NPC selecionado/spawnado, percorrendo
as três propriedades de estado em `CPlayer+0x26C0` com stride `0x20`. O seletor não consulta
relógio nem contador de cooldown; o bloqueio é um summon ativo por slot.

O lifecycle de liberação também está fechado. `FUN_350E9AE0`, chamado no início da morte normal
`FUN_350EE230`, da morte especial `FUN_350F7AB0` e do desaparecimento `FUN_350E9B30`, exige
`entityType == 2` e owner não nulo, resolve `owner+0x26C0+slot*0x20` e grava estado `0`. No ciclo
seguinte, `FUN_351DBF00` promove esse estado para `1` somente se o CP atual cobrir o custo; caso
contrário ele permanece `0`. Não existe estado ou timer intermediário de cooldown.

A recarga de CP comprovada é orientada a combate. No encerramento válido do NPC,
`FUN_350EE230` lê o campo runtime `NpcSetup[level]+0x64`, carregado da série `uint16`
`creatures.dat+0x166E`, chama `AddCP` e, para o jogador local responsável, `EmitCPMessage`.
Para Nak, a curva começa em `20, 21, 22, 23, 24...` CP. Isso prova ganho por morte; não foi
encontrada regeneração passiva por tempo no código nativo nem no corpus Lua desta build.

## Ownership, times, friendly fire e alvo

`DecompileClientNpcTargeting.py` fecha a regra comum usada pelos summons e NPCs de stage. A
identidade de time está em `MovableEntity+0x264`:

| Faixa | Predicado original |
|---:|---|
| `0..9` | `IsRedTeam` |
| `10..19` | `IsBlueTeam` |
| `20..255` | `IsGrayTeam` |

`IsEnemy @ 0x351DDDD0` rejeita primeiro entidades com o mesmo byte `+0x264`. Em modo Deathmatch
(`session+0x1A3 == 2`), qualquer slot diferente é inimigo. Nos demais modos, dois cinzas não são
inimigos; dois slots coloridos são inimigos somente quando um é vermelho e o outro azul; misturar
cinza com colorido é inimigo. Portanto o dono e aliados do mesmo time ficam fora de targeting e
friendly fire no cliente original.

O NPC guarda o master em `CNpcBase+0x620`; `SetSpawnVariable @ 0x350E26B0` resolve esse ponteiro a
partir do spawn. `IsHaveMasterPlayer @ 0x350E17C0` devolve o seat `+0x264` de Player/NpcBase e, nas
partidas por time, consegue escolher um player ativo dentro de `0..9` ou `10..19` quando precisa
representar o lado do NPC.

`IsValidForEnemy @ 0x350E24C0` exige candidato não nulo e vivo, diferente do master, aceito por
`IsEnemy` e que não seja `MapItem` nem `BoxItem`. `IsValidReceiveDamage @ 0x350E5130` exige o NPC
vivo, fora dos dois estados de bloqueio `+0x5AC/+0x5B8`, atacante diferente dele mesmo e, para
Player/NpcBase, relação inimiga. O bone flag `NoDamage_Switch` bloqueia dano; `NpcChocolateCake`
é a exceção explícita a esse flag. O overload `0x350DD3C0` também rejeita dano `<= 0`.

`CNpcWatcher` implementa a escolha de alvo host-side:

- considera entidades de runtime `9/10` que sejam Player ou derivadas de NpcBase;
- exclui o próprio owner, seu master `+0x620` e NPCs que compartilhem o mesmo master;
- exige flags `0x8` e ausência de `0x1000`, cone de visão, linha de visada e distância até
  `owner+0x584`;
- usa `watcher+0x130` como ângulo: entrada `<= 0,1` vira `10`, entrada `>= 179` vira `179` e os
  valores intermediários são preservados;
- modos `0/1` comparam distância; `2/3` comparam a propriedade virtual do candidato;
- modos `0/3` escolhem o menor valor e `1/2`, o maior;
- LongBow tenta player voando, depois primeiro NPC e player; IceWind tenta primeiro NPC e depois
  player; as demais classes usam a busca de player do setor;
- quando o vencedor muda e é melhor que o alvo atual, `SendWatchEvent` publica a troca e
  `CNpcBase::SetTarget` mantém referência forte em `+0x368`; o prior target fica em `+0x7F8`.

Isso fecha owner, separação de times, friendly fire e a política base de aquisição/troca de alvo.
A primeira família específica também está fechada estaticamente em
[`npc-family-nak.md`](npc-family-nak.md): as quatro variantes compartilham 29 eventos locais,
`Shoot_Poison`, perseguição, reação a impacto, morte e faixa própria `3.0f`. As demais famílias
começam em [`npc-family-panzer.md`](npc-family-panzer.md), que fecha 34 eventos locais, ataques
`Attack_01/02` e seleção por distância `0.6/0.9`, e em
[`npc-family-crossbow.md`](npc-family-crossbow.md), que fecha ataque próximo, tiro, projétil e a
submáquina própria da segunda variante, e em [`npc-family-blazer.md`](npc-family-blazer.md), que
fecha o ataque de FireBall, alcance, efeitos das mãos e transições. As famílias após Blazer
continuam sendo comportamento por classe, não uma lacuna dessa política comum.

A série `uint32[99]` em `creatures.dat+0x1A4C` começa em `300, 340, 380...` para Nak e alimenta o
campo runtime `NpcSetup+0x8C`. O loader prova a largura de quatro bytes; a leitura anterior como
`uint16` foi corrigida. A busca dos consumidores recuperados não encontrou leitura direta desse
offset. Portanto ela está **carregada, mas sem consumidor estático direto nesta build**; não é
custo CP e não deve receber regra server-side especulativa. O extrator a preserva como
`unconsumed_field_1a4c` para comparação futura. O servidor persiste os dois stats de personagem,
porém não mantém CP atual, custo de
summon, regeneração, destruição de cell ou cooldown.

## `npcinfo`

Schema:

```text
npc   int
level int
exp   int
gold  int
```

O dump contém 3.570 linhas, níveis `1..255` e 14 tipos distintos:

```text
0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 15, 18, 21, 33
```

O tipo é exatamente `itemId-8000`; os valores esparsos indicam quais itens possuem curva nessa
base, não outra enumeração de criatura. Assim, itens ativos `8013`, `8016`, `8022` e `8025` não
possuem respectivamente os tipos `13`, `16`, `22` e `25` em `npcinfo`. A coluna `gold` vale `1`
em todo o dump auditado; não se deve tratá-la como drop direto sem localizar o consumidor
original.

Ausências estruturais:

- sem primary/unique key;
- MyISAM, sem transação;
- sem HP, ataque, defesa, velocidade, custo CP ou cooldown;
- sem vínculo explícito a item ID, `.ecl` ou stage;
- sem regra de drop/owner.

O World carrega `npcinfo(npc,level,exp)` no boot para `FUN_0040B940`. Para os três slots de cell
`10..12`, o índice original é `(itemId-8000)*200+level`, o ganho por resultado é limitado a 100
e o nível 99 usa o teto da curva. EXP/nível são persistidos em `useriteminfo` na mesma transação
de EXP/gold do personagem, com ledger idempotente por match/round/character/cell index.

## Arquitetura do engine do cliente

O stage usa a Serious Engine e diferencia:

- player local/remoto;
- general NPC;
- map NPC;
- Master Golem;
- map items.

Criação de avatar remoto não é um pacote World solto: ocorre por entity-message reliable
`ADDPLAYER type 7`, após o game state estar carregado. NPCs usam outro dispatcher `0x3xx`.
Injetar `0x4B/0x4C` como spawn é incorreto; esses são senders cliente→World de add-player/reply.

## Dispatcher NPC `0x307..0x312`

`CSessionState::HandleMessage` trata:

| Opcode | Semântica confirmada | Conteúdo principal |
|---:|---|---|
| `0x307` | Create general NPC | índices/tipo, entity ID, placement, init blob |
| `0x308` | Create Master Golem | team/index, entity ID, placement, init blob |
| `0x309` | Create map NPC | map index, entity ID, placement, init blob |
| `0x30A` | player action/movement | action serializada e placement |
| `0x30B` | placement/state de entidade | entity kind/index, posição compactada, estado |
| `0x30C` | message event | origem/tipos, alvo/ID, payload variável |
| `0x30F` | ação de player/entidade | um slot, chamada virtual no player remoto |
| `0x310` | pedir create de map NPC ausente | target seat, kind `3`, map index |
| `0x312` | map item list/status | lista variável de itens do mapa |

O `0x30B` usa um discriminador de entidade:

- `2`: general NPC, indexado por dois componentes;
- `3`: map NPC;
- `4`: Master Golem.

O `0x308` reliable contém `[u8 hostSlot][u8 teamIndex][u16 entityField]`, seis componentes de
placement com quatro bytes cada e um init blob serializado. O `0x30B` contém
`[u16 timing/state][u8 kind][u8 group][u8 index][s16 x][s16 y][s16 z][s16 heading]`. O init blob
variável ainda precisa de golden capture para os valores runtime; o envelope, os campos fixos e a
gramática escalar descrita abaixo estão fechados.
O sender chama o slot virtual `entity.vtable+0x04` para escrever uma `CNetMessage` aninhada. No
receiver de `0x0307`, os 28 bytes fixos são lidos primeiro, o restante é extraído como outra
`CNetMessage`, `AddRemoteGeneralNpc @ 0x361097A0` cria a classe indicada por `entityField` e o slot
virtual `entity.vtable+0x118` consome o blob antes de `CEntity::Initialize`. Logo ele não é uma
struct única do protocolo: é serialização polimórfica da classe compilada em `Entities.dll`.

`tools/extract_entity_init_serializers.py` cruzou os 47 caminhos configurados, os manifests, os
exports e as vtables da imagem PE32 mapeada localmente. O resultado fecha três famílias para as
43 classes presentes sem depender de um processo ou dump de memória:

| Família | Classes | Writer `vtable+0x114` | Reader `vtable+0x118` |
|---|---:|---:|---:|
| `CNpcBase` | 41 | `0x350E3FA0` | `0x350E96E0` |
| `CNpcGoldGolem` | 1 | `0x35100DC0` | `0x35100EB0` |
| `CNpcChocolateCake` | 1 | `0x350F4EE0` | `0x350F4FE0` |

Os quatro `NpcBlackDragon*` ficam fora da tabela porque seus manifests e exports não existem.
O módulo ativo usado na reprodução tem SHA-256
`3235f03638a87779afeec43fd975d0f9bf49825551a32acb48795fa15372d621`.
O writer comum de `0x0307`, `FUN_350E1590`, grava owner/index, `entityField`, seis `float32` de
placement e chama `vtable+0x114` para a cauda; o receiver usa `vtable+0x118`.

`DecompileClientEntityInitSerializers.py` cruzou os ponteiros runtime com os exports de
`engine.dll`: `0x36004B00/0x36004A00` são os operadores `float32`,
`0x36004B80/0x36004A80` são os operadores `u8` e `0x36004BA0/0x36004AA0` são os operadores
`u32` de `CNetMessage`. Com isso, a gramática escalar do init blob fica fechada:

```text
CNpcBase:
  f32 property_26c, f32 property_7b0, u8 property_7c4
  u8 textLength, u8 text[textLength]
  if entityType == 3: u8 owner_264, u8 ownerClass, u8 ownerIndexA,
                      u8 ownerIndexB, u8 ownerIndexC
  u8 linkedEntityState, u32 property_7d0

CNpcGoldGolem:
  f32 property_38ec, f32 property_38e8, u8 isAlive
  if entityType == 3: u8 owner_264, u8 ownerClass, u8 ownerIndexA,
                      u8 ownerIndexB, u8 ownerIndexC

CNpcChocolateCake:
  f32 property_38e4, f32 property_38e0, u8 isAlive
  if entityType == 3: u8 owner_264, u8 ownerClass, u8 ownerIndexA,
                      u8 ownerIndexB, u8 ownerIndexC, u32 property_7d0
```

Writer e reader são simétricos. O terceiro campo das duas famílias derivadas chama o export
`CNpcBase::IsAlive @ 0x350DCDA0`; quando chega como zero, o reader constrói
`ENpcBaseDeath @ 0x350DC650`. Na família Base, `linkedEntityState` não é cópia de `+0x620`:
o writer emite `0` quando esse ponteiro está ausente, `1` quando o helper de resolução retorna
valor não zero e `2` quando retorna zero; o receiver armazena o resultado em `+0x800`.
Os nomes de domínio dos floats e de `property_7d0` ainda dependem de consumidores não fechados,
mas tipo, ordem, condições e domínio dos bytes de controle não dependem mais de captura. Os
setters derivados confirmam defaults parciais: Chocolate Cake inicia
`+0x38E4/+0x38E0` em `1.0f`; Gold Golem inicia `+0x38EC/+0x38E8` em `0.0f`; ambos iniciam
`+0x7B0=0.0f`, `+0x7C4=1`, `+0x620=0` e `+0x7D0=0`. O texto usa comprimento `u8`, logo seu
limite estrutural é 255 bytes. Isso ainda não produz uma golden completa porque `property_26c`, o
texto, owner e valores ligados ao estado podem mudar depois da inicialização.

`tools/decode_entity_init_blob.py` materializa essa gramática com consumo integral obrigatório.
Ele rejeita truncamento e bytes excedentes, explicita o sentinel `ownerClass=0xFF` e aceita
`base`, `gold_golem` ou `chocolate_cake` junto do `entityType`. Exemplo:

```powershell
python tools/decode_entity_init_blob.py base 3 <init-blob-hex>
```

`0x307` e `0x309` usam o mesmo prefixo de criação de `0x308`:

```text
0x307: u8 ownerSlot, u8 npcIndex, u16 entityField, 6 x 4-byte placement, init blob
0x309: u8 hostSlot,  u8 mapIndex, u16 entityField, 6 x 4-byte placement, init blob
```

`SendInfoCreateNpcTo` percorre os nove NPCs do owner local; `SendInfoCreateMapNpcTo` percorre até
`0x41` map NPCs. Ambos serializam a própria entidade e enviam reliable ao peer que está entrando.

O envelope `0x30C` é:

```text
u8  sourceSlot
u8  entityClass
u8  indexA
u8  indexB
u32 eventType
u32 payloadLength
u8  payload[payloadLength]
```

`HandleMsgEvent` resolve `entityClass` `1` player, `2` general NPC, `3` map NPC, `4` Master
Golem, `6` entidade indexada e `7` map/box item. O payload é a representação binária do evento
da classe.

O inventário gerado [`entity-event-catalog.md`](../../protocol/entity-event-catalog.md) cruza os
exports com o dump runtime e fecha ID e tamanho total das 269 classes `E*` exportadas, sem falhas.
O ID não é global: eventos genéricos podem repetir o mesmo número porque `entityClass` e o tipo da
entidade participam da resolução. Para a família base de NPC `0x044D`, construtores e helpers
`ClearToDefault` fecham também a estrutura física dos payloads (`pad` são bytes de alinhamento):

| Event ID | Classe | Payload físico após os 8 bytes-base |
|---:|---|---|
| `0x044D0000..0004` | Stand/Approach/Close/Range/Idle | vazio |
| `0x044D0005` | `ESoulShot` | `vec3f, vec3f, u8, u8, u8, pad1` |
| `0x044D0006` | `ESetTargetforGroup` | `CEntityPointer, CEntityPointer` (`2 × u32`) |
| `0x044D0007` | `EReportTargetforLeader` | `CEntityPointer, CEntityPointer` (`2 × u32`) |
| `0x044D0008` | `ESpawnNpc` | `CEntityID, u32` |
| `0x044D0009` | `ENpcReconsiderBehavior` | vazio |
| `0x044D000A` | `ENpcBaseDeath` | `u32, u32, u32` |
| `0x044D000B` | `EGoldSword` | `u8, u8, pad2` |
| `0x044D000C` | `ENpcDisappear` | vazio |
| `0x044D000D` | `ENpcBaseDamage` | `u32, vec3f, vec3f, u32, u32, u32, u8, pad3` |
| `0x044D000E` | `ENpcHP` | `u32, u8, pad3` |
| `0x044D000F..0011` | AttackHit/AttackFire/TouchRemote | `u32, u8, u8, pad2` |
| `0x044D0012` | `EMovementAnimation` | `u32` |
| `0x044D0013` | `EBeIdle` | vazio |
| `0x044D0014` | `ENpcDeadToSwitch` | `CEntityPointer` (`u32`) |
| `0x044D0015` | `EMasterGolemDamage` | `u32, u32` |
| `0x044D0016` | `ENpcExtraSet` | `u8, pad3, u32` |
| `0x044D0017..0018` | SetDummy/SetNormal | vazio |

`0x352B346C` resolve para `ClearToDefault(Vector<float,3>)`, `0x352B34D4` para
`ClearToDefault(CEntityPointer)`, `0x352B3470` para `ClearToDefault(CEntityID)` e
`0x352B3474` para o construtor de `CEntityID`. Isso prova os tipos acima; nomes de domínio dos
`u32/u8` restantes dependem dos produtores/consumidores e não foram inventados.

Os `.ecl` de `Classes.xfs` são apenas manifests de pacote/classe, por exemplo
`npcnak.ecl → Entities.dll/CNpcNak1` e `summoner.ecl → Entities.dll/CSummoner`; não contêm o
bytecode de gameplay. A tabela `BasicEffectType_values @ 0x3537FC20` em `entitiesmp.dll` fecha os
seguintes tipos de efeito relevantes. Esses valores não são event IDs de `0x30C`:

`tools/ghidra/DumpBasicEffectTypes.py` reproduz a tabela completa em
`C:\temp\basic_effect_types.txt`.

| ID | Nome compilado |
|---:|---|
| `0x30` | `Summoner star explosion` |
| `0x44` | `Weapon Cell` |
| `0x48` | `Summon Npc` |
| `0x49` | `Disappear Npc` |
| `0x53` | `HP Charge Effect` |
| `0x54` | `Fear Charge Effect` |
| `0x55` | `Steam Charge Effect` |
| `0x56` | `Steam2 Charge Effect` |
| `0x57` | `AP Charge Effect` |
| `0x58` | `Scouter Effect` |
| `0x59` | `CP Charge Effect` |
| `0x5A` | `Chaos Charge Effect` |

`FUN_3508BCB0` inicializa a propriedade de evento com `0x49`. A tabela canônica corrige a
atribuição anterior: esse valor representa `Disappear Npc`; `Summon Npc` é `0x48`. No caminho
de desaparecimento,
`FUN_350E9B30` primeiro libera o slot por `FUN_350E9AE0`, lê o custo efetivo em
`owner+0x26C8+slot*0x20` e chama `AddCP(custo × 0,3)` antes de ocultar/desabilitar a entidade. A
constante em `0x352B7F8C` é `0.3000000119f`. Isso fecha um **refund de 30% do custo efetivo no
desaparecimento**; ele não é a recompensa de morte, que vem de `NpcSetup+0x64` em
`FUN_350EE230`. Os layouts internos dos payloads continuam variáveis por classe.

`0x312` contém `[u8 count]` seguido de `count` pares `[u8 mapItemIndex][u8 state]`. O host monta
essa lista de entidades `MapItem`/`BoxItem` e a envia reliable no snapshot de late join.

Quando um `0x30B` kind `3` aponta para map NPC ausente, o cliente emite reliable `0x310`; kind
`4` ausente cria o par de Master Golems. O World não deve produzir esses corpos: eles pertencem
ao host/game session e trafegam no canal P2P reliable direto.

O corpo exato do `0x310` é `[u8 targetSeat][u8 entityKind=3][u8 mapIndex]`. O receiver resolve o
Map NPC e envia seu `0x0309` ao `targetSeat`. O fallback valida os três bytes, comprimento exato e
repetição do seat autenticado; a ordem sintética antiga `[target, index, kind]` foi removida.

No fio UDP direto, o bit reliable transforma esses tipos em
`0x8307/0x8308/0x8309/0x830B/0x830C/0x8310/0x8312`.
O prefixo de transporte é `[u16 type|0x8000][u32 sequence][u8 sourceSlot]`; após ACK `0x4000`, o
receiver remove o bit e entrega o tipo logical ao dispatcher desta seção.

`GameplayPeerDatagramCodec` reconhece esses sete tipos no fallback UDP. Parsers tipados extraem
owner/host, índice, `entityField`, seis floats e fronteira do init blob de `0x8307/08/09`; o
`0x830B` extrai timing, kind `2/3/4`, índices e quatro `s16`; o `0x8310` exige os três bytes exatos;
e o `0x8312` materializa no máximo `0x41` pares. `0x830C` continua validando rota e tamanho interno.
Seats do transporte e dos corpos aninhados são comparados ao peer autenticado antes do fan-out,
com rate limit e isolamento por field. `0x030A/0x030F/0x0311` ficam no codec tipado de ações
unreliable, não são ausências do allowlist reliable.

## Criação e sincronização

`CSessionState::AddRemotePlayer @ 0x3610E2B0` é o único chamador das quatro rotinas de snapshot.
Se o slot ainda não existe, ele executa, nesta ordem:

1. cria o player remoto e, quando há blob de entrada, aplica `CPlayer::ApplyInitData` nele;
2. serializa o player local com `CPlayer::GetInitData` e o envia diretamente ao novo peer;
3. chama `SendInfoCreateNpcTo` para sincronizar NPCs existentes;
4. somente quando o índice local `world+0x470C` é igual ao boss `world+0x2E`, envia, nessa ordem,
   map NPCs, Master Golems e map item status.

Isso demonstra que a autoridade da sessão/host mantém o estado necessário e monta um snapshot
direcionado no momento do late join. O servidor .NET não deve duplicar esse cache no modo
compatível: ele transporta `TunnelOne 0x57` ao peer exato e `TunnelAll 0x54` aos peers elegíveis,
enquanto entity-message type 7 e montagem/replay do snapshot continuam no master do cliente.

Funções relevantes do engine:

| Endereço | Função |
|---:|---|
| `0x36109BE0` | `SendInfoCreateNpcTo` |
| `0x36109CC0` | `SendInfoCreateMapNpcTo` |
| `0x3610B1E0` | `SendInfoCreateMasterGolemTo` |
| `0x3610C7C0` | `CreateMasterGolem` |
| `0x3610D060` | `BuildMapItemList` |
| `0x3610D6A0` | `SendInfoMapItemStatus` |
| `0x3610E2B0` | `CSessionState::AddRemotePlayer` |
| `0x360C4830` | `CWorld::CreateNpc` |

Os quatro alvos têm um único caller e a ordem acima aparece no mesmo bloco do engine. Assim, o
contrato estático de late join está encerrado; o que permanece é confirmar o resultado visual em
uma sessão real de dois clientes, não descobrir outro opcode ou cache no World.

## Autoridade atual por cenário

### Stage solo

O cliente carrega stage, cria NPCs e conduz IA/combate localmente. O World recebe apenas eventos
de fim e recompensa informada pelo cliente. Não há validação de:

- composição/waves;
- spawn e morte;
- dano e HP;
- objetivo;
- CP gasto;
- drop;
- tempo real de clear.

### PvP/cells

O servidor mantém apenas score/field e relay de pacotes opacos. Não existe instância de criatura,
owner, team, HP, AI ou custo CP. Portanto summons podem no máximo existir visualmente no cliente;
o backend não os autoriza nem determina resultado.

### Multi-cliente

O transporte `0x8307..0x8312` está implementado em modo host/P2P-authoritative: bytes válidos de
um endpoint autenticado chegam somente aos outros peers do mesmo field. O World não conserva
estado de entidade, conforme a autoridade original. O caminho direcionado necessário ao late
join está implementado e testado no `TunnelOne`; a montagem pelo master e a reconstrução no novo
cliente estão fechadas estaticamente, mas ainda carecem de validação visual com dois clientes.

## Lacunas dinâmicas do RE

O formato e os fluxos estão fechados estaticamente, mas estes comportamentos ainda precisam de
observação/instrumentação do runtime real:

- timings, animações e ataques das famílias após Blazer; targeting, Nak, Panzer, CrossBow e
  Blazer estão fechados;
- dano causado/recebido e “Cell destruction”;
- valores concretos dos eventos de morte/despawn;
- EXP/gold por kill via `npcinfo`;
- drops/map items e pickup;
- waves/objetivos por stage;
- reconstrução visual em reconexão/late join; a sequência estática já está fechada;
- Master/Gold Golem e Golden Sword.

## Extensão server-authoritative opcional

Autorizar custo CP, simular IA/HP, determinar drops e validar cada kill no World não existe no
servidor original desta build. Isso seria uma camada moderna de autoridade/anti-cheat, não uma
condição para fidelidade do RE host-authoritative.

## Auditoria atual

| Componente | Estado |
|---|---|
| assets/classes cliente | 47 tipos configurados, 43 classes carregáveis; `NpcBlackDragon*` ausentes; snapshot externo de 51 incompatível |
| `creatures.dat` | layout 47 × 99 níveis e tabela secundária fechados; EXP e custo GOLD nomeados |
| catálogo item/classe/tipo NPC | fechado para conteúdo ativo; três aliases de stages inativos não têm item |
| `npcinfo` | presente, 3.570 linhas; EXP consumida pela progressão de cell `0x50` |
| Max CP/Cell destruction | persistidos como stats |
| CP runtime | atual, máximo, clamp, morte, custo e débito mapeados no cliente; client-authoritative como no original |
| summon | três slots, estados `0/1/2`, rejeição, débito, spawn, liberação e refund de 30% fechados no cliente |
| entidade/IA/HP | ownership, friendly fire, dano e targeting comum fechados; Nak/Panzer/CrossBow/Blazer fechadas estaticamente, demais famílias e efeitos/hitboxes exatos pendentes |
| protocolo `0x307..0x312` | envelopes tipados/validados e relayados; três famílias de init blob identificadas; `0x310` corrigido |
| map items | cliente/host identificados; backend ausente conforme arquitetura original |
| stage solo | client-authoritative |
| sincronização multi-cliente | relay e TunnelOne aprovados; snapshot de late join fechado estaticamente, visual pendente |
| testes | codec, truncamento, source forjado, cross-field e relay runtime aprovados |

## Arquitetura escolhida para compatibilidade

A arquitetura do v258 e o caminho escolhido para compatibilidade são host-authoritative:

- um cliente host executa engine/IA;
- World autentica membership e relaya mensagens allowlisted;
- snapshots `0x307/308/309/312` permitem late join;
- resultados econômicos continuam calculados/validados no World.

Ela preserva o comportamento original. O risco de cheating do host deve ser tratado como requisito
de uma feature moderna separada, sem reclassificar o RE como incompleto.

### Alternativa moderna, fora do RE fiel

Mais segura, porém muito maior:

```text
CreatureCatalog
CellInventory / CellLoadout
SummonService
NpcInstance { EntityId, Type, Level, Owner, Team, HP, State, Position }
NpcSimulation / AI
StageScriptRuntime
EntityProtocolAdapter
```

O World calcula spawn, movimento, dano, morte, drop e recompensa. O cliente apenas renderiza e
envia intenção. Essa opção exige reconstruir layouts e comportamento de cada família.

### Próxima ordem de fechamento do RE

1. Capturar corpos reais de `0x307/308/309/30C/312` para transformar os layouts estáticos das três
   famílias de init em golden bytes e correlacionar valores runtime dos eventos. Os 269 pares
   ID/tamanho, a estrutura física de `0x044D` e os efeitos `Summon Npc`, `Disappear Npc` e
   `CP Charge Effect` já estão fechados.
2. Substituir os fixtures sintéticos dos codecs por golden captures reais de cada envelope.
3. Validar visualmente o snapshot de late join montado pelo master através de `TunnelOne`.
4. Correlacionar os floats/propriedades ainda sem nome usando os captures reais.
5. Validar Nak, Panzer/Golem e ranged em duas sessões.
6. Manter recompensa econômica idempotente no World sem confiar em métricas fora do limite já fechado.
7. Tratar autoridade server-side somente como feature separada e default-off.

## Ativação atual

Não existe seção `[Creatures]` na configuração. O recorte implementado é parte do relay UDP e fica
ativo quando as portas `[UDP] Port1/Port2` estão ativas. Ele apenas fornece fallback de transporte;
não habilita summon, CP, IA, rewards ou snapshot server-side.

Para validar, rode `python tools/world_udp_probe.py 40708` com `test/test2/test3`. A prova envia
os sete envelopes, exige bytes idênticos no peer do mesmo field e ausência no sender e em outro
field. Snapshot `0x8312` truncado e source seat forjado precisam ser descartados.

Uma futura simulação deve nascer desativada e começar com Nak sem recompensa. Só após validação
visual de summon, movimento, ataque, morte e despawn deve habilitar custo CP ou reward. Rollback
não pode remover itens persistidos dos jogadores.

## Prova headless — 2026-07-15

`world_udp_probe.py` executou três sessões em dois fields contra o World Release. Além dos onze
shapes já cobertos de ação/controle, enviou create general NPC `0x8307`, Master Golem `0x8308`,
map NPC `0x8309`, entity state `0x830B`, map NPC action `0x8310` e map item snapshot `0x8312`.
Todos chegaram byte a byte apenas ao peer do mesmo field. Um `0x8312` com count/comprimento
divergente foi descartado. As fixtures `test2/test3` foram removidas ao final.

## Testes mínimos

- confirmar por instrumentação dinâmica se a série dormente `+0x1A4C` é lida por script/reflection;
- regressão do lifecycle: slot `2→0→1`, limite de três slots e refund de 30% com clamp;
- spawn/despawn e entity ID único;
- golden captures das três famílias de init blob, valores representativos de eventos e validação
  visual de `0x307..0x312`;
- posição compactada, limites e payload truncado;
- owner/team e target inválido como regressão da política já mapeada;
- dano, morte, reward e duplicação de evento;
- late join recebe visualmente o snapshot uma vez; o caller, as guardas e a ordem já estão fechados;
- disconnect do host/owner e transferência de autoridade;
- map items/pickup sem duplicação;
- visual com dois clientes para cada família habilitada;
- stage solo não concede recompensa forjada.

## Critério de conclusão do RE v258

Catálogo, mappings, regras client-side de CP/summon, APIs Lua, CP inicial, late join estático e
autoridade original já estão confirmados.
Para marcar o RE dinâmico completo ainda faltam goldens runtime das três famílias, correlação dos
valores variáveis, late join observado e validação visual multi-cliente das criaturas habilitadas.
Uma simulação server-authoritative não faz parte desse critério; se criada, deve possuir plano,
feature flag e validação próprios.
