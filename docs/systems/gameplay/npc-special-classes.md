# Classes NPC especiais — MasterGolem, GoldGolem e ChocolateCake — Rakion v258

## Escopo e veredito

Este documento fecha o passe estático das três classes NPC que não pertencem a nenhuma família do
`creaturelist.txt` com variantes: `NpcMasterGolem`, `NpcGoldGolem` e `NpcChocolateCake`. As três
derivam **diretamente** de `CNpcBase` (não de uma base de família) e têm máquina local própria.

**Veredito:** MasterGolem e GoldGolem compartilham o mesmo desenho — dois ataques herdados do
fluxo comum, selector por três alcances e um estado de morte que NÃO destrói a entidade: ela
congela aguardando os eventos de respawn/rebirth do objetivo de partida. ChocolateCake é o oposto:
não ataca — só possui idle, uma animação disparada pelo fluxo comum e uma morte que distribui o
efeito do evento aos players. Com essas três, **toda classe `Npc*` concreta do `entitiesmp.dll`
tem passe dedicado** (censo em [`entity-class-census.md`](../../audits/entity-class-census.md)).

## Golden source e reprodução

- `Bin/entitiesmp.dll`, SHA-256
  `3235f03638a87779afeec43fd975d0f9bf49825551a32acb48795fa15372d621`;
- imagem runtime Ghidra `entmemoryfast/entitiesmp_dump.bin`;
- `tools/ghidra/DecompileClientNpcMasterGolem.py`, `DecompileClientNpcGoldGolem.py` e
  `DecompileClientNpcChocolateCake.py` (extrator compartilhado `NpcFamilyExtractor.py`);
- censo de descritores: `tools/ghidra/DumpEntityClassCensus.py`.

O protocolo de criação/objetivo (0x0307/0x0308/0x0310, `EGoldSword`, ownership e late join) já
está fechado em [`golem-boss-objectives.md`](golem-boss-objectives.md); o init blob tipado de
`CNpcGoldGolem`/`CNpcChocolateCake`, em [`cells-creatures-npc.md`](cells-creatures-npc.md). Este
documento fecha o que faltava: as máquinas locais.

## Classes

| Classe | ID | Descritor | Factory | Alocação | Ctor | Pai |
|---|---:|---:|---:|---:|---:|---|
| `NpcMasterGolem` | `0x0465` | `0x3538BF38` | `0x35115350` | `0x3928` | `0x351142C0` | `CNpcBase` |
| `NpcGoldGolem` | `0x0469` | `0x3538B0E8` | `0x35101660` | `0x3930` | `0x35100D50` | `CNpcBase` |
| `NpcChocolateCake` | `0x049C` | `0x3538A790` | `0x350F4D10` | `0x3990` | `0x350F4630` | `CNpcBase` |

## NpcMasterGolem (`0x0465`)

Tabela local `0x3538BDC8`, estados `0001..0014` e default. Propriedades: `+0x38E0`/`+0x38E4`
bools e `+0x38E8`/`+0x38EC` floats. Componentes: quatro modelos e dois sons. Overrides:

| Evento | Base | Handler | Responsabilidade |
|---:|---:|---:|---|
| `0x04650001` | `0x044D0043` | `0x35114A80` | ataque A: som compartilhado `0x352B9280`, anim canal 8 |
| `0x04650004` | `0x044D0044` | `0x35114C70` | ataque B: emissor de som dedicado (volume `5,0`, alcance `50,0`), liga `+0x7C0` |
| `0x04650007` | `0x044D0055` | `0x35116FC0` | **morte de objetivo** (abaixo) |
| `0x0465000A` | `0x044D0039` | `0x351156E0` | selector nível 1: alcance `+0x57C`, probe `30`, cooldown `+0x588` |

A seleção é em cascata: `000A` (perto, `+0x57C`) agenda ataque A por `000B`; falha cai em `0014`
(`0x35115840`), que testa `+0x580` e desvia para `0011/0013`; falha cai em `0012` (`0x35115940`),
que testa `+0x584` com probe `5,0` e agenda o ataque B por `000D`; sem alcance nenhum, `0010`
espera um tick e reconverge.

A morte (`0x35116FC0`) não remove a entidade:

1. para movimento/som e liga a animação de destruição (canal 8, sinal `2`);
2. credita CP ao matador via `CPlayer::AddCP` e emite a mensagem de CP;
3. resolve o time do Master Golem destruído e publica as mensagens localizadas
   `0x68..0x6D` (aliado/inimigo/próprio), com as texturas `Msg_AttackedMasterGolem.tex`,
   `MasterGolemAttackedByFriendly.tex` e os halos `Halo_MasterGolem_Red/Blue.tex`;
4. entra em `0008` com espera infinita (`-1`), onde só reage a `EMasterGolemDamage`
   (`0x044D0015`), aos hooks de HP (`0x50013/0x50014`, barra
   `EFNMTexturesSV\UI\MasterGolemHP\*`) e a `EMasterGolemRespawn` (`0x04650000`), que
   restaura o estado e devolve ao loop comum `0x044D0064`.

O default publica o efeito compilado `0x40000207` e toca o som componente `0x0465000A`.

## NpcGoldGolem (`0x0469`)

Tabela local `0x3538AF78`, estados `0002..0015` e default — `0x04690000/0x04690001` são os
eventos `EGoldGolemRespawn`/`EGoldGolemRebirth`, por isso a máquina começa em `0002`. O desenho
espelha o MasterGolem (mesmos quatro overrides, deslocados uma posição): ataque A em `0002`,
ataque B em `0005`, selector em `000B`, morte em `0008`.

Propriedades: bools `+0x38E0`/`+0x38E4`, floats `+0x38E8..+0x38F4` e o bool de vida `+0x38F8`
(o `isAlive` do init blob 0x0307). Componentes: sete modelos e dois sons.

A morte (`0x35104ED0`) zera `+0x38F8`, congela a entidade, publica
`Msg_DefeatingGoldGolem.tex` e entra em `0009` (`0x351058D0`), o estado de dormência do modo
Golden Sword:

- `EGoldSword` (`0x044D000B`) é encaminhado a `SetGoldSwordModeForPlayer` (`0x350E9A40`) —
  a posse da espada segue o contrato já fechado em
  [`golem-boss-objectives.md`](golem-boss-objectives.md);
- `EGoldGolemRespawn` (`0x04690000`) religa `+0x38F8 = 1` e devolve ao loop comum;
- `EGoldGolemRebirth` (`0x04690001`) publica `GoldGolemRespawned.tex` com mensagem localizada e
  devolve ao loop comum;
- `0x50013/0x50014` cobrem os hooks de render (radar `GoldGolem.tex`, halo
  `Halo_GoldGolem.tex`).

## NpcChocolateCake (`0x049C`)

Tabela local `0x3538A700`, só sete registros — a menor máquina NPC da build. Propriedades:
quatro floats `+0x38E0..+0x38EC` (os dois primeiros viajam no init blob) e um sound object
`+0x38F0`. Componentes: um modelo (`ChocolateCake.SMC`) e um som. Não há estados de ataque,
perseguição ou selector: o bolo é um alvo estático de evento.

| Evento | Base | Handler | Responsabilidade |
|---:|---:|---:|---|
| `0x049C0000` | `0x044D0055` | `0x350F7AB0` | morte: distribui o efeito do evento |
| `0x049C0001` | — | `0x350F49B0` | idle: espera sinal `6` ou tick |
| `0x049C0003` | `0x044D0046` | `0x350F4A80` | animação disparada pelo fluxo comum, canal 8 |
| `0x049C0004/5` | — | — | esperam fim de animação e reconvergem |

A morte (`0x350F7AB0`) só executa a distribuição quando `entityType == 3` (map NPC): percorre os
20 slots de player da sessão, separa os elegíveis por classe (`0x352BB738`/`0x352D0B94`) e aplica
`FUN_350E0C00` com códigos distintos por ramo, além do efeito "Chocolate Cake Hit Effect". Os
valores concretos do benefício (HP/buff) ficam no consumidor `0x350E0C00` e dependem de captura
runtime para nomear com segurança.

## Contrato de implementação

```text
ObjectiveGolem { team, alive, respawnAt }         // MasterGolem/GoldGolem: estado do objetivo
GoldenSword    { holderId?, mode }                // já contratado em golem-boss-objectives
CakeEvent      { entityId, rewardKind }           // ChocolateCake: distribuição na morte
```

O World já sintetiza o objetivo Golem/Boss pelos contratos de `golem-boss-objectives.md`; estas
máquinas são a apresentação client-side desses objetivos. Nenhuma exige duplicação server-side
além do que os handlers `0x4D/0x60` e o placar já cobrem.

## Limite de validação

Fechado estaticamente: classes, IDs, tamanhos, ctors, propriedades, componentes, os três
conjuntos de estados, overrides, morte-como-objetivo, dormência com respawn/rebirth e a
distribuição do bolo. Ainda dinâmico: valores concretos do benefício do bolo, cadência visual dos
dois ataques dos Golems e as barras/halos em partida real.
