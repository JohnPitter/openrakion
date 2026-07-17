# Censo de classes de entidade — `entitiesmp.dll` v258

Reprodução: `tools/ghidra/DumpEntityClassCensus.py` sobre a imagem runtime
`entmemoryfast/entitiesmp_dump.bin` (`Bin/entitiesmp.dll`, SHA-256
`3235f03638a87779afeec43fd975d0f9bf49825551a32acb48795fa15372d621`). O script varre a região de
dados por registros `[nome*, binder 0x352B42D5, class_id, parent_def*, factory*]` — o layout do
`CDLLEntityClass` compilado — e encontrou **116 descritores concretos**. Nenhuma outra classe
instanciável existe no módulo: todo `AddRemoteGeneralNpc`/factory passa por um desses registros.

Este censo é a prova de fechamento do passe residual: cada linha tem veredito e fonte canônica.
Os eventos por classe estão em [`entity-event-catalog.md`](../protocol/entity-event-catalog.md).

## Vereditos

- **NPC — família documentada**: as 41 classes `Npc*` de família (10 bases + variantes + classes
  de projétil `SoulCannon`/`LongBow`/`IceWind` + auxiliares `NpcGolemStoneDebris`,
  `NpcProjectile`, `NpcBasicEffect`, `NpcWatcher`, `NpcBase`) — docs `npc-family-*.md`,
  [`cells-creatures-npc.md`](../systems/gameplay/cells-creatures-npc.md) e
  [`npc-special-classes.md`](../systems/gameplay/npc-special-classes.md).
- **NPC — classe especial documentada**: `NpcMasterGolem` (`0x0465`), `NpcGoldGolem` (`0x0469`)
  e `NpcChocolateCake` (`0x049C`) — máquinas locais fechadas em
  [`npc-special-classes.md`](../systems/gameplay/npc-special-classes.md); protocolo/objetivo em
  [`golem-boss-objectives.md`](../systems/gameplay/golem-boss-objectives.md).
- **Player**: `Player`, `Player Weapons`, `Player View`, `Player Animator` — reescritos pela
  Rakion sobre o esqueleto SE1; contratos em
  [`combat-actions-status.md`](../systems/gameplay/combat-actions-status.md) e
  [`pvp-modes-combat.md`](../systems/gameplay/pvp-modes-combat.md).
- **Skill de mago**: `BlessBall`, `MagicBomb`, `MagicMissile`, `FireEff`, `MageHold`
  (`0x3CAC..0x3CB0`) — conteúdo Rakion; eventos tipados no catálogo, produtores nos eventos de
  arma do player.
- **Efeito/infra Rakion**: `BasicEffect`, `CPEffect`, `Freeze`, `FloatingScore`,
  `ExplosionEffect`, `BillBoardImage`, `WeaponEffect*` (`0x0426..0x042B`), `SoulCharge`,
  `Passive`, `RangeWeapon`, `Indicator` — apresentação client-side; eventos no catálogo.
- **Item/evento**: `Item`, `ChristmasBox` (`0x52B3`), `EventItem` (`0x52B5`) — docs de
  [`christmas-and-gifts.md`](../systems/events/christmas-and-gifts.md) e
  [`valentine-and-generic-events.md`](../systems/events/valentine-and-generic-events.md).
- **Herança SE1 (Serious Sam)**: `Enemy Base/Fly/Run Into/Dive`, `LarvaOffspring`,
  `Serious Bomb`, `Spinner`, `Twister`, `Projectile`, `Bullet`, `Flame`, `Cannon ball`,
  `SpawnerProjectile`, `Debris` (×2), `Blood Spray`, `BloodEmitter`, `BloodStain`, `Spine`,
  `SpineJut`, `Watcher`, `Reminder`, `ParticleSeed`, `ParticleMessage`, `MessageManager`,
  `ModelHolder`, `Effector` — nomes idênticos ao source aberto da SE1/EntitiesMP. Permanecem
  compiladas e registradas, sem produtor Rakion documentado; não são lacuna de RE, são a base
  herdada. Nenhum stage v258 spawna essas classes ([`cells-creatures-npc.md`]).

## Tabela completa

| class_id | Classe | Pai | Veredito |
|---:|---|---|---|
| `0x00CB` | `ModelHolder` | engine | herança SE1 |
| `0x0136` | `Enemy Base` | engine | herança SE1 |
| `0x0137` | `Enemy Fly` | `Enemy Base` | herança SE1 |
| `0x0138` | `Enemy Run Into` | `Enemy Base` | herança SE1 |
| `0x0139` | `Enemy Dive` | `Enemy Base` | herança SE1 |
| `0x015C` | `Spinner` | engine | herança SE1 |
| `0x0161` | `LarvaOffspring` | engine | herança SE1 |
| `0x0162` | `Serious Bomb` | engine | herança SE1 |
| `0x0191` | `Player` | engine | player (reescrito Rakion) |
| `0x0192` | `Player Weapons` | engine | player (reescrito Rakion) |
| `0x0193` | `Player View` | engine | player (reescrito Rakion) |
| `0x0196` | `Player Animator` | engine | player (reescrito Rakion) |
| `0x01F5` | `Projectile` | engine | herança SE1 |
| `0x01F6` | `Bullet` | engine | herança SE1 |
| `0x01F8` | `Flame` | engine | herança SE1 |
| `0x01FA` | `Cannon ball` | engine | herança SE1 |
| `0x01FB` | `SpawnerProjectile` | engine | herança SE1 |
| `0x0200` | `Twister` | engine | herança SE1 |
| `0x0259` | `BasicEffect` | engine | efeito/infra Rakion |
| `0x025A` | `Debris` | engine | herança SE1 |
| `0x025B` | `Blood Spray` | engine | herança SE1 |
| `0x0260` | `Effector` | engine | herança SE1 |
| `0x0269` | `Debris` (segunda) | engine | herança SE1 |
| `0x026A` | `FloatingScore` | engine | efeito/infra Rakion |
| `0x0270` | `ExplosionEffect` | engine | efeito/infra Rakion |
| `0x0271` | `CPEffect` | engine | efeito/infra Rakion |
| `0x0272` | `Freeze` | engine | efeito/infra Rakion |
| `0x02BC` | `Watcher` | engine | herança SE1 |
| `0x02BF` | `Reminder` | engine | herança SE1 |
| `0x0320` | `Item` | engine | item/evento |
| `0x03EA` | `ParticleSeed` | engine | herança SE1 |
| `0x03EB` | `ParticleMessage` | engine | herança SE1 |
| `0x03EC` | `MessageManager` | engine | herança SE1 |
| `0x041A` | `RangeWeapon` | engine | efeito/infra Rakion |
| `0x041B` | `BloodEmitter` | engine | herança SE1 |
| `0x041C` | `BloodStain` | engine | herança SE1 |
| `0x041D` | `Spine` | engine | herança SE1 |
| `0x041E` | `SoulCharge` | engine | efeito/infra Rakion ([`npc-family-soulcannon.md`](../systems/gameplay/npc-family-soulcannon.md)) |
| `0x041F` | `SpineJut` | engine | herança SE1 |
| `0x0420` | `BillBoardImage` | engine | efeito/infra Rakion |
| `0x0421` | `Passive` | engine | efeito/infra Rakion |
| `0x0426..0x042B` | `WeaponEffect*` (6) | base própria | efeito/infra Rakion |
| `0x044D` | `NpcBase` | engine | NPC — política comum documentada |
| `0x044F` | `NpcWatcher` | engine | NPC — targeting documentado |
| `0x0457` | `NpcBasicEffect` | engine | NPC — efeito de spawn (catálogo) |
| `0x0459` | `NpcProjectile` | engine | NPC — projétil genérico (Dragon) |
| `0x045A` | `NpcGolemStoneDebris` | engine | NPC — auxiliar Golem documentado |
| `0x0461..0x049B` | famílias `Npc*` (41) | bases próprias | NPC — famílias documentadas |
| `0x0465` | `NpcMasterGolem` | `NpcBase` | NPC — especial documentado |
| `0x0469` | `NpcGoldGolem` | `NpcBase` | NPC — especial documentado |
| `0x046E` | `SoulCannon` (projétil) | engine | NPC — projétil SoulCannon |
| `0x0470` | `LongBow` (projétil) | engine | NPC — projétil LongBow |
| `0x049A` | `IceWind` (projétil) | engine | NPC — projétil IceWind |
| `0x049C` | `NpcChocolateCake` | `NpcBase` | NPC — especial documentado |
| `0x3CAC` | `BlessBall` | engine | skill de mago |
| `0x3CAD` | `MagicBomb` | engine | skill de mago |
| `0x3CAE` | `MagicMissile` | engine | skill de mago |
| `0x3CAF` | `FireEff` | engine | skill de mago |
| `0x3CB0` | `MageHold` | engine | skill de mago |
| `0x52AF` | `Indicator` | engine | efeito/infra Rakion |
| `0x52B3` | `ChristmasBox` | engine | item/evento |
| `0x52B5` | `EventItem` | engine | item/evento |

A saída integral (endereços de descritor, factory e tabelas) é regenerável pelo script; a tabela
acima agrupa as linhas de família para leitura. IDs de classe são únicos — não existem duas
classes com o mesmo `class_id`.

## `NpcBlackDragon*` — ausência formal

O censo não contém nenhum descritor `NpcBlackDragon`, `NpcBlackDragon2/3/4` nem qualquer outro
nome fora da tabela acima. Somado à ausência de manifest/exports
([`cells-creatures-npc.md`](../systems/gameplay/cells-creatures-npc.md)) e de entrada em
`Classes.xfs`, o veredito é definitivo: os quatro caminhos `NpcBlackDragon*` do
`creaturelist.txt` são **conteúdo configurado sem classe carregável** nesta build. O alias
`blackdragon` dos stages inativos `49..55` referencia apenas o caminho.

## Critério de completude

Com este censo:

1. toda classe concreta do `entitiesmp.dll` tem veredito (documentada, herança SE1, player,
   skill, efeito ou item/evento);
2. nenhum `Npc*` ficou sem passe dedicado — 10 famílias, 3 especiais e 8 auxiliares/projéteis;
3. os aliases de conteúdo (`black*`, `bloodnak`, `skyblazer`, `irongolem`, `assaultpanzer`)
   resolvem para variantes já documentadas — são skins/dados, não máquinas próprias;
4. ausências (`NpcBlackDragon*`, itens 8012/8036/8039/8042) estão comprovadas por três fontes
   independentes (censo, manifests/exports, `items.dat`).
