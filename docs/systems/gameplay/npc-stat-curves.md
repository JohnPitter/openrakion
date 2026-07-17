# Curvas de atributos de NPC e Cell — Rakion v258

## Veredito

`DataSetup.xfs/datasetup/creatures.dat` é a fonte canônica dos atributos por tipo e nível. Cada
uma das 47 criaturas possui 24 séries de 99 valores, serializadas em 8.118 bytes e expandidas
pelo cliente para registros runtime de `0xA0` bytes. O layout fecha byte a byte; não há padding
ou série adicional desconhecida dentro do bloco.

A tela de Cell do cliente prova dez nomes: `Attack`, `Armor`, `Energy`, `Speed`, `Attack Speed`,
`Vision Range`, `Speed (distance)`, `Attack Speed (d.)`, `Recovery Time` e `Cell Point`.
`CNpcBase::FillDamageInfoParam @ 0x350E9550` prova que `Attack` é o valor-base usado para montar
o dano, antes dos multiplicadores do ataque/animação. `Armor` é zero nos 4.653 pontos da fonte
v258. `Energy` é a vitalidade-base exibida, mas o binário não expõe uma segunda curva de AP de
NPC: portanto não se deve converter `Armor` em AP nem inventar AP server-side.

O World .NET ainda não executa a simulação de NPC. Estas curvas são necessárias para uma futura
autoridade PvE, mas hoje continuam pertencendo ao host cliente, como o restante do runtime de
Cells/NPCs.

## Evidência reproduzível

- `CPlayer_ReadNpcDataCore @ 0x35228D10` lê exatamente 47 tipos, níveis `1..99`, e grava cada
  nível em `(type * 0x66 + level) * 0xA0`;
- os helpers `0x3522B340/370/380/390/400` confirmam larguras serializadas de 1, 2 ou 4 bytes;
- `FUN_00451DE0` calcula o mesmo índice e desenha os dez campos do painel;
- os `PUSH 0x1D2..0x1DB` antes de `GetLanguageStr` ligam os campos aos IDs `466..475` de
  `language.txt`;
- os formatos do painel são `%u`, `%u`, `%u`, `%.1f`, `%.1f`, `%.0f`, `%.1f`, `%.1f`,
  `%.1f`/`-` e `%u`;
- `tools/ghidra/DecompileClientNpcStats.py` reproduz loader, painel, dano e recompensa CP nos
  projetos do cliente e do dump runtime de `entitiesmp.dll`;
- `tools/extract_cell_catalog.py --data-setup-xfs` exporta as 24 séries, seus offsets e os
  labels do próprio XFS.

## Layout completo de `creatures.dat`

Cada linha representa uma série contígua com 99 valores. `raw32` preserva quatro bytes sem
atribuir semântica ao tipo C++ original. Os nomes `unknown_*` são intencionais: o loader os
consome, mas ainda não há consumidor de domínio que permita nomeá-los.

| Campo exportado | Offset arquivo | Tipo | Offset runtime | Label/uso provado |
|---|---:|---|---:|---|
| `unknown_runtime_00` | `0x0000` | raw32 | `+0x00` | desconhecido |
| `cumulative_cell_exp` | `0x018C` | `uint32` | `+0x08` | limiar acumulado de EXP |
| `attack` | `0x0318` | `uint16` | `+0x14` | `466 Attack`; base de dano |
| `armor` | `0x03DE` | `uint16` | `+0x20` | `467 Armor`; todos os valores são zero |
| `energy` | `0x04A4` | `uint16` | `+0x2C` | `468 Energy`; vitalidade-base exibida |
| `unknown_runtime_34` | `0x056A` | `float32` | `+0x34` | desconhecido |
| `speed` | `0x06F6` | `float32` | `+0x38` | `469 Speed` |
| `unknown_runtime_3c` | `0x0882` | `float32` | `+0x3C` | desconhecido |
| `attack_speed` | `0x0A0E` | `float32` | `+0x40` | `470 Attack Speed` |
| `vision_range` | `0x0B9A` | `float32` | `+0x44` | `471 Vision Range` |
| `distance_speed` | `0x0D26` | `float32` | `+0x48` | `472 Speed (distance)` |
| `unknown_runtime_4c` | `0x0EB2` | `float32` | `+0x4C` | desconhecido |
| `distance_attack_speed` | `0x103E` | `float32` | `+0x50` | `473 Attack Speed (d.)` |
| `unknown_runtime_54` | `0x11CA` | `float32` | `+0x54` | desconhecido |
| `recovery_time` | `0x1356` | `float32` | `+0x58` | `474 Recovery Time` |
| `unknown_runtime_5c` | `0x14E2` | `float32` | `+0x5C` | desconhecido |
| `npc_kill_cp_reward` | `0x166E` | `uint16` | `+0x64` | CP concedido na morte |
| `summon_cp_cost` | `0x1734` | `uint16` | `+0x70` | `475 Cell Point`; custo do summon |
| `unknown_runtime_7c` | `0x17FA` | `uint16` | `+0x7C` | desconhecido |
| `upgrade_gold` | `0x18C0` | `float32` | `+0x84` | custo GOLD de upgrade |
| `unconsumed_field_1a4c` | `0x1A4C` | `uint32` | `+0x8C` | carregado, sem leitor direto provado |
| `unknown_runtime_94` | `0x1BD8` | `uint16` | `+0x94` | desconhecido |
| `unknown_runtime_98` | `0x1C9E` | raw32 | `+0x98` | desconhecido; valor bruto `1` em toda a fonte |
| `unknown_runtime_9c` | `0x1E2A` | raw32 | `+0x9C` | desconhecido |

O fim da última série é `0x1FB6`, exatamente 8.118 bytes. Depois dos 47 blocos principais,
`creatures.dat` contém `47 × 4 × 33 = 6.204` bytes de registros secundários já delimitados pelo
extrator; eles não fazem parte do registro por nível.

### Correção do campo `0x1A4C`

O loader usa o helper de quatro bytes em `0x1A4C`, e a série seguinte começa em `0x1BD8`:
`0x1BD8 - 0x1A4C = 396 = 99 × 4`. Logo esse campo é `uint32[99]`, não `uint16[99]`.
O extrator anterior lia apenas a primeira metade da série; ele foi corrigido e o teste agora usa
`70000` para impedir regressão silenciosa para 16 bits.

## Curvas das dez famílias base

Valores abaixo são os pontos dos níveis 1, 50 e 99 extraídos da golden source v258. Eles mostram
que ataque, energia e economia escalam de forma independente; interpolar uma curva a partir de
outra produziria resultados incorretos.

| Tipo | Attack L1/L50/L99 | Energy L1/L50/L99 | CP kill L1/L50/L99 | CP summon L1/L50/L99 |
|---|---:|---:|---:|---:|
| Nak | 90/286/365 | 50/146/195 | 20/78/137 | 400/1576/2752 |
| Panzer | 90/286/365 | 180/621/863 | 30/118/206 | 600/2364/4128 |
| CrossBow | 100/345/459 | 140/483/675 | 32/128/223 | 650/2561/4472 |
| Blazer | 100/345/459 | 160/552/735 | 55/216/378 | 1100/4334/7568 |
| Golem | 140/483/646 | 400/1380/1984 | 100/394/688 | 2000/7880/13760 |
| SoulCannon | 130/424/563 | 280/964/1418 | 100/394/688 | 2000/7880/13760 |
| LongBow | 110/355/466 | 300/1053/1512 | 60/236/412 | 1200/4728/8256 |
| Taurus | 50/146/195 | 300/1053/1512 | 130/512/894 | 2600/10244/17888 |
| IceWind | 40/138/190 | 250/838/1175 | 75/295/516 | 1500/5910/10320 |
| Dragon | 90/286/365 | 260/897/1254 | 150/591/1032 | 3000/11820/20640 |

As variantes `2..4`, Master/Gold Golem e Chocolate Cake possuem suas próprias 99 entradas e são
exportadas integralmente no JSON. Não se deve sintetizá-las multiplicando as famílias base.

## Limite de implementação

O mapeamento fecha armazenamento, labels, curvas e consumo do dano-base. Ainda não é uma
simulação PvE autoritativa: faltam portar para o backend os multiplicadores por animação/ataque,
colisão, estados, projéteis, resistências e decisões de IA das 43 classes carregáveis. Até isso
existir, usar apenas `attack` e `energy` no World criaria uma autoridade parcial incompatível com
o host cliente.
