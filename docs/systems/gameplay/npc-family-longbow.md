# Família NPC LongBow — Rakion v258

## Escopo e veredito

Este documento fecha o passe estático de `NpcLongBow`, `NpcLongBow2`, `NpcLongBow3` e
`NpcLongBow4`. Embora compartilhe conceitos de flecha com CrossBow, LongBow possui classe base,
targeting e máquina de eventos próprios.

**Veredito:** as quatro variantes compartilham `CNpcLongBowBase` e 22 eventos locais. O ciclo
anexa a flecha em `R_Weapon`, carrega, seleciona target até `50`, valida visada com probe `20`,
dispara e remove/recicla a flecha visual. Há um ramo específico de acompanhamento de target aéreo
que atualiza posição, altura e prazo antes de voltar ao tiro. Modelo/curva distinguem variantes;
não existem quatro IAs.

## Golden source e reprodução

- `Bin/entitiesmp.dll`, SHA-256
  `3235f03638a87779afeec43fd975d0f9bf49825551a32acb48795fa15372d621`;
- imagem runtime Ghidra `entmemoryfast/entitiesmp_dump.bin`;
- `tools/ghidra/DecompileClientNpcLongBow.py`;
- extrator compartilhado `tools/ghidra/NpcFamilyExtractor.py`.

O extrator lê descritores, tabela `0x3538B948`, handlers, default, propriedades, assets e o helper
de criação/configuração da flecha.

## Classes

| Classe | ID compilado | Descritor | Factory | Pai |
|---|---:|---:|---:|---:|
| `NpcLongBow` / `CNpcLongBow1` | `0x046F` | `0x3538B7F8` | `0x3510E550` | `CNpcLongBowBase` `0x3538BAB8` |
| `NpcLongBow2` / `CNpcLongBow2` | `0x0483` | `0x3538B858` | `0x3510E880` | igual |
| `NpcLongBow3` / `CNpcLongBow3` | `0x0484` | `0x3538B8B8` | `0x3510EBB0` | igual |
| `NpcLongBow4` / `CNpcLongBow4` | `0x0485` | `0x3538B918` | `0x3510EEE0` | igual |
| `CNpcLongBowBase` | `0x0492` | `0x3538BAD8` | `0x351102F0` | `CNpcBase` |

Os quatro `SetDefaultProperties` saltam para o default único `0x3510F000`. A curva de atributos
permanece em `creatures.dat`.

## Defaults e seleção

| Campo runtime | Valor | Uso |
|---:|---:|---|
| `+0x57C` | `2.0f` | limiar próximo herdável |
| `+0x580` | `10.0f` | limiar intermediário herdável |
| `+0x584` | `50.0f` | alcance usado pelo selector LongBow |
| `+0x588` | `2.0f` | delay curto |
| `+0x58C` | `3.0f` | cooldown do tiro |
| probe de visada | `20.0f` | validação do target no tiro e acompanhamento |

O handler `000B` exige target em `CNpcBase+0x368`, distância menor que `+0x584` e probe válido.
Quando passa, atualiza orientação, agenda `+0x5BC` com `+0x58C` e entra em `000C`. Falha segue
para `000F`, preservando espera/retorno sem disparo.

## Máquina local `0x0492`

A tabela contém `0000..0015` mais default. Quatro eventos substituem a base:

| Evento LongBow | Evento base | Handler | Responsabilidade |
|---:|---:|---:|---|
| `0x04920000` | `0x044D0046` | `0x3510F820` | anexa/mostra `ARROW` e abre ciclo de carga |
| `0x04920008` | `0x044D0044` | `0x35110350` | prepara animação, `R_Weapon` e disparo |
| `0x0492000B` | `0x044D0039` | `0x35110570` | targeting, alcance, visada e cooldown |
| `0x04920010` | `0x044D0034` | `0x35110090` | captura posição para acompanhamento aéreo |

`0001..0007` compõem carga, transições `0x50003/04` e espera. `0009/000A` finalizam o disparo.
`000C..000F` validam a animação de tiro e retornam ao selector. `0010..0014` mantêm o ramo aéreo:
capturam posição, verificam target/altura, atualizam direção e retornam a `000B` quando o prazo
expira ou a visada fica válida. `0015` converge para `0x044D0064`, o loop comum.

O default encaminha eventos não locais para a máquina comum. A escolha global de target já
documentada em [`cells-creatures-npc.md`](cells-creatures-npc.md) dá prioridade a player voando,
depois NPC e player; esta máquina implementa o comportamento após essa escolha.

## Assets

| Uso | Recurso |
|---|---|
| modelo | `EFNMModelsSV\NPC\Longbow\Longbow.smc` |
| flecha | `ModelsSV\NPC\Longbow\Weapon\Arrow.SMC` |
| attachment | `ARROW`, `R_Weapon` |
| carga | `SoundsSV\Npcs\NpcLongbow\LoadLongbow.wav` |
| disparo | `SoundsSV\Npcs\NpcLongbow\ShootLongbow.wav` |
| ciclo | `Summon.wav`, `Die.wav`, `Run.wav` |

Os handlers selecionam a animação por chamadas virtuais e estado do target. Os literais globais
`Attack_01/02` não são referenciados diretamente pela tabela LongBow; por isso o nome final da
animação não é tratado como provado neste passe.

## Contrato de implementação

```text
LongBowDefinition { npcType, level, projectileRange = 50, attackDelay = 3, statCurve }
LongBowState { entityId, targetId, localEventId, attackDeadline, aerialAnchor }
LongBowArrow { ownerId, targetId, origin, direction, spawnedAt }
```

Targeting, acompanhamento aéreo, spawn e dano pertencem ao backend em uma porta autoritativa.
Modelo, attachment e áudio ficam no cliente. O mesmo hit não deve ser autoritativo no host
original e no World simultaneamente.

## Limite de validação

Fechado estaticamente:

- cinco classes, IDs, factories, herança e defaults;
- 22 eventos, default e quatro overrides;
- flecha, attachment, carga, disparo, alcance `50`, cooldown `3` e probe `20`;
- ramo de target aéreo e retorno ao selector;
- modelo e sons de ciclo.

Ainda dinâmico:

- nome/animação final selecionada pelas chamadas virtuais;
- frame de soltura, velocidade, gravidade, lifetime e colisão da flecha;
- volume de hit e multiplicador de dano;
- posição visual durante acompanhamento aéreo;
- variantes 2/3/4 e sincronização em duas sessões.

Esses dados precisam de captura real e não devem ser inferidos dos assets.
