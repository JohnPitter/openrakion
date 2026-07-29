# Engenharia reversa de ações e estados de combate — Rakion v258

## Escopo e veredito

Este documento separa o tráfego de ação em tempo real do controle de partida no World e mapeia
ataque, movimento, hit, HP/AP/CP, morte, respawn, invencibilidade, velocidade e estado de rede.
Modos e placar estão em [`pvp-modes-combat.md`](pvp-modes-combat.md); transporte em
[`../core/udp-p2p-tunneling.md`](../core/udp-p2p-tunneling.md); células e NPCs em
[`cells-creatures-npc.md`](cells-creatures-npc.md).

**Veredito:** o pipeline peer/engine foi fechado para `0x030A/0x030F/0x0311`, bad ping e eventos
reliable de entidade `0x830C`. O cliente original calcula dano, reduz HP/AP, publica HP/AP, morte e
respawn como eventos P2P; o World retransmite o datagrama e só recebe por TCP `0x4F` o reporte de
morte usado no placar. O backend .NET agora valida o envelope e o tamanho interno de `0x830C`,
decodifica o snapshot de HP/AP e mantém a autoridade compatível como relay. Ele não tenta inventar
uma simulação server-side que não existe nessa build.

## Fontes e limite da análise

- `tools/ghidra/DecompileClientActionStreams.py`, executado sobre `engine.dll`;
- `tools/ghidra/DecompileClientActionProducer.py`, aplicado ao dump runtime de `entitiesmp.dll`
  e ao `gamemp.dll`;
- `tools/ghidra/DumpEnumValues.py`, aplicado às tabelas runtime de `entitiesmp.dll`;
- `tools/ghidra/TraceClientReliableCallers.py` e `TraceRakionReliableImports.py`;
- captura pareada de 2.593 updates `0x030A` e 2.589 companheiros `0x030F`;
- exports de `worldnet.dll`, especialmente `SendFieldGameDiePlayer` e `SendFieldGameExit`;
- handlers `Op_0x46_Recon` e `Op_0x4F_Recon`;
- `Domain.Field`, `PlayerRec` e relay UDP atuais;
- vtable do player no engine, com `GetMaxHP`, `GetHP`, `SetHP`, `AddHP` e `ReduceHP`;
- stats persistidos em `characterinfo` e flags dos scripts do cliente.
- dump runtime de `entitiesmp.dll`, SHA-256
  `18E6359BA27BEEB5345F8C550B73ED838B0715BE2E293659637970891F67E140`;
- dump runtime desembrulhado de `gamemp.dll`, SHA-256
  `305AEE110506385E4C2FB5FF47AB7763D2E5FDCC2F19643451B95081222F2538`;
- `tools/ghidra/DecompileClientPlayerCombat.py`, `DecompileEngineEntityEvents.py`,
  `DecompileGameRespawnSettings.py` e `DecompileSessionPropertyConsumers.py`;
- `tools/ghidra/DecompileClientWeaponEvents.py`, com os construtores, cópias, produtores,
  dispatcher e consumidores de arma/hold;
- `tools/ghidra/DumpInstructionRanges.py`, usado para confirmar os offsets que o decompilador
  não preserva com clareza em parâmetros passados por valor.
- o golden source público do Serious Engine, em
  [`NetworkMessage.h`](https://github.com/Croteam-official/Serious-Engine/blob/b408e88a16fd01aa1cfd0e0a999c86c2c1437c9e/Sources/Engine/Network/NetworkMessage.h#L274-L280),
  [`Game.cpp`](https://github.com/Croteam-official/Serious-Engine/blob/b408e88a16fd01aa1cfd0e0a999c86c2c1437c9e/Sources/GameMP/Game.cpp#L438-L460)
  e [`Player.es`](https://github.com/Croteam-official/Serious-Engine/blob/b408e88a16fd01aa1cfd0e0a999c86c2c1437c9e/Sources/EntitiesMP/Player.es#L446-L527),
  usado apenas depois de confirmar o mesmo export e o fluxo correspondente na build v258.

O layout dos três streams de ação está comprovado por decompilação e captura. Os shapes
`0x0304/0x0305/0x0319/0x4000/0x830C/0x8313/0x8315` estão fechados para classificação e relay.
`0x4000` é ACK do transporte, `0x830C` é evento logical `0x030C` e `0x8313` é o estado reliable
de bad ping. Os cinco eventos reliable de troca/disparo/hold agora também possuem layout tipado;
os poucos words sem nome de domínio no binário continuam deliberadamente neutros.

### `0x8313` — bad ping

O bit `0x8000` é acrescentado pelo transporte ao tipo lógico `0x0313`. O produtor
`rakion.bin:0x0045C6F0` envia dois bytes de payload:

```text
u16 type=0x8313 | u32 sequence | u8 transportSource
u8 playerSeat | u8 badPingFlag
```

O consumidor no `case 0x0313` de `rakion.bin:0x00411760` resolve `playerSeat` e chama
`CPlayer::SetBadPing(flag)`. `FUN_0045CE60` exige amostras dos outros players, usa RTT `>199 ms`
e considera ruim quando pelo menos `ceil((playing-1)/2)` peers ultrapassam o limite. Cada janela
precisa de dez amostras por peer e é avaliada em intervalos de 10 s. A entrada em bad ping exige
duas janelas ruins consecutivas; uma janela saudável publica `flag=0`. Isso é indicador visual
P2P do cliente, não prova de hit/dano nem motivo server-side de kick.

### `0x830C` — evento reliable de entidade

`CEntity::SendEvent @ engine.dll:0x36128A90` recebe `CEntityEvent` e chama
`CNet::SendToOtherClientReliable(..., 0x030C, ...)`. O transporte acrescenta `0x8000`; por isso o
tipo observado é `0x830C`. Depois do header reliable, o envelope é:

```text
u16 type=0x830C | u32 sequence | u8 transportSourceSeat
u8 senderSeat
u8 route                 1, 2, 3, 4, 6 ou 7
u8 primaryEntitySeat
u8 secondaryEntitySeat
u32 entityEventId
u32 payloadLength
u8  payload[payloadLength]
```

O tamanho total deve ser exatamente `19 + payloadLength`. Os dois samples antes tratados apenas
como shapes de 23/31 bytes confirmam o mesmo envelope. Rotas `1..4/6/7` correspondem às categorias
de entidade selecionadas pelos quatro predicados virtuais de `CEntity`; o World não interpreta a
categoria, apenas preserva o pacote para os outros peers.

Eventos de player comprovados em `entitiesmp.dll`:

| ID | classe | tamanho total da classe | corpo no `0x830C` |
|---:|---|---:|---:|
| `0x0191000B` | `EPlayerDamage` | `0x30` | `0x28` |
| `0x0191000C` | `EPlayerRemainHP` | `0x14` | `0x0C` |
| `0x01910016` | `EPlayerDeath` | `0x14` | `0x0C` |
| `0x01910017` | `ERespawn` | `0x08` | `0` |

Os oito bytes removidos do corpo são a base `CEntityEvent`: vtable e `entityEventId`. O codec .NET
aceita qualquer evento de entidade com rota e comprimento consistentes, para não quebrar NPCs e
itens, e oferece decodificação tipada de `EPlayerRemainHP` e dos cinco eventos abaixo.

### Arma, disparo e hold em `0x830C`

Layouts exatos do corpo, depois do `payloadLength`:

```text
0x01910006 ESetWeapon
  i32 weaponSelector | i32 argument

0x01910007 EShootWeapon
  vec3f firstVector | vec3f secondVector
  u8 shootType | u8 reserved[3]

0x01910008 EShootShuriken
  vec3f firstVector | vec3f secondVector
  u8 projectileCount | u8 variant | u16 reserved

0x01910009 ERequestHoldAttack
  u32 entityWord | u8 entityIndex | u8 entitySubIndex | u16 reserved
  f32 maximumDistance | u32 argument

0x0191000A EHoldAttack
  u32 entityWord | u8 entityIndex | u8 entitySubIndex | u16 reserved0
  u32 argument | u8 actorIndex | u8 actorSubIndex | u16 reserved1
```

`CPlayerAnimator::SetWeapon(ESetWeapon)` usa `weaponSelector` para escolher os dois caminhos de
arma. `EShootWeaponType` possui valores compilados `0..2`; o produtor grava esse valor em
`shootType`. `CPlayerWeapons::RequestShootWeapon(long, Vector, Vector)` monta
`EShootShuriken` com dois vetores e `projectileCount=9`; `ShootShuriken` usa o byte como limite
real do loop de projéteis. `CheckHoldAttack` usa `maximumDistance` na checagem geométrica antes de
emitir `EHoldAttack`, e `ExecuteHoldAttack` resolve a entidade e encaminha a execução ao alvo.

Os nomes `firstVector`, `secondVector`, `entityWord` e `argument` são intencionalmente neutros:
os offsets e os usos estão fechados, mas a build não preserva nomes de membros que distingam, em
todos os tipos de tiro, origem de impacto, direção e identificador composto. O decoder expõe também
padding/reservados porque eles fazem parte do tamanho transmitido e não devem ser descartados em
análise byte a byte.

### `EPlayerDamage 0x0191000B`

O corpo de 40 bytes também está fechado:

```text
u32 playerId
u8 damageType | u8 damageMotionType | u16 reserved
f32 firstDamageValue | f32 secondDamageValue
vec3f firstVector | vec3f secondVector
```

`CPlayerWeapons::SetupDamageInfo` grava `DamageType` e `DamageMotionType` em `DamageInfo+0x50/+0x54`
e os dois vetores em `+0x58/+0x64`. `CPlayer::ReceiveDamage` copia esses campos sem conversão para
o evento, grava a identidade local em `playerId` e calcula os dois valores escalares a partir dos
coeficientes `u16` de `DamageInfo+2/+4`, multiplicadores ativos e propriedades do atacante. Em
seguida, o mesmo evento por valor entra em `ApplyReceiveDamage`, que termina em
`WorkReduce_HP_AP` e publica o snapshot de HP/AP.

Os escalares permanecem `firstDamageValue/secondDamageValue`, em vez de receber rótulo definitivo
HP/AP, porque o decompiler perde a convenção de chamada de `WorkReduce_HP_AP`. Os dois vetores
também permanecem neutros até uma captura ou consumidor distinguir inequivocamente impacto de
direção. Tipo, motion, offsets, larguras e caminho de aplicação não estão mais pendentes.

`EPlayerDeath 0x01910016` possui corpo `vec3f deathVector`. No caminho em que o dano mata o player,
`ReceiveDamage` copia para esse evento os mesmos três words do primeiro vetor de `DamageInfo+0x58`.
`ERespawn 0x01910017` não possui payload. Ambos agora têm validação tipada sem alterar a política
peer-authoritative do World.

## Duas autoridades diferentes

O protocolo original não transforma o World em simulador de cada golpe:

```text
cliente/engine origem
  -> stream peer de ação, movimento e teclas
  -> vítima monta EPlayerDamage e aplica redução local de HP/AP
  -> CEntity::SendEvent publica dano/HP/AP/morte/respawn por 0x830C
  -> engine dos demais clientes aplica o mesmo evento de entidade
  -> cliente vítima reporta a morte por TCP 0x4F
  -> World valida estado alto nível, pontua e broadcasta a morte
```

Portanto, afirmar que o World original calcula todo dano seria incorreto. Porém, reproduzir apenas
essa confiança entre peers também deixa o servidor atual vulnerável. A implementação de lançamento
deve escolher explicitamente entre compatibilidade peer-authoritative monitorada ou combate
server-authoritative. Misturar os dois sem uma fonte final produz mortes e placares divergentes.

## Stream do engine

### `0x030A` — ação e movimento

Layout exato de 26 bytes:

```text
u16 type=0x030A | u32 sequence | u8 transportSource
u16 deltaMilliseconds
u8 packedSourceAndState | u8 actionCode
s16 positionX | s16 positionY | s16 positionZ
s16 angleWord | u8 angleByte
s16 viewRotationX | s16 viewRotationY | s16 viewRotationZ
```

`packedSourceAndState & 0x1F` repete o seat e `(packedSourceAndState >> 5) & 3` seleciona a enum
`PlayerActionState`: `0 Normal`, `1 Attack`, `2 Damage`, `3 Nostate`. `actionCode` usa a enum
`ePlayerAction` compilada:

| Faixa | Ações |
|---|---|
| `0..3` | None, Stand, Idle00, Idle01 |
| `4..11` | Forward, Backward, Left, Right e quatro diagonais |
| `12..16` | Jump, Land, Rise, RollFront, RollBack |
| `17..26` | Guard, StruckGuard e oito movimentos em guard |
| `27..31` | troca para weapon 1/2, TryHold, TurnLeft, TurnRight |

`CSessionState::GetActionFromMessage @ 0x3610AFE0` lê os 19 bytes nessa ordem e recompõe um
`CPlayerAction` de 72 bytes. O produtor mantém dez snapshots anteriores dessa estrutura. O trio
final é `pa_aViewRotation`, não velocidade nem um vetor genérico. `SendAction @ 0x36103940` lê o
trio em `CPlayerSource+0x90/+0x94/+0x98`, isto é, `CPlayerAction+0x38/+0x3C/+0x40` dentro do
snapshot guardado em `CPlayerSource+0x58`. `ctl_ComposeActionPacket @ entitiesmp.dll:0x35139310`
copia para esses offsets os acumuladores locais `+0xAB0/+0xAB4/+0xAB8` e os zera após compor o
pacote; o assembly grava zero explicitamente no componente central nesse caminho. Por fim,
`CPlayer::ActiveActions @ 0x35151300` consome exatamente `+0x38/+0x3C/+0x40` nos cálculos
angulares/direcionais.

O golden source do mesmo engine nomeia os três vetores de ação como `pa_vTranslation`,
`pa_aRotation` e `pa_aViewRotation`; `CControls::CreateAction` alimenta o último com
`AXIS_LOOK_LR/UD/BK`, e `ctl_ComposeActionPacket` acumula e reaplica `m_aLocalViewRotation`.
A build Rakion acrescenta placement, estado e ação compacta à estrutura, mas preserva esse último
trio e o mesmo export. A captura mostra cadência próxima de 100 ms e sequência compartilhada com
os streams companheiros.

O `0x030A` só deve ser aceito depois que a sessão peer do jogo foi carregada e o peer correspondente
existe. Eventos reliable de load/connect precedem gameplay. O World não deve tentar interpretar
esse opcode como um comando TCP de lobby.

### `0x030F` — snapshot de sincronização do jogador

Shape confirmado de 14 bytes: header comum de 7 bytes seguido por `u8 sourceEcho` e seis campos
`u8`. `CPlayerSource::SendSyncData @ engine.dll:0x36103040` chama o slot virtual `+0x1BC`,
exportado como `CPlayer::GetSyncData(CNetMessage&)`; `HandleMessage @ 0x3610D7C0` resolve o peer e
chama o slot `+0x1C0`, exportado como `CPlayer::ApplySyncData(CNetMessage&)`.

O produtor `entitiesmp.dll:0x3513A200` serializa, na ordem: resultado de `IsAlive`, valor reduzido
de `CPlayer+0x2AD8`, byte do animator `+0x128`, valor reduzido de `CPlayer+0x2B8C`, modo em
`CPlayer+0x2904` e detalhe `+0x2908` somente quando o modo é zero. Os nomes de domínio dos quatro
últimos offsets não aparecem nos símbolos desta build e permanecem neutros no DTO. O consumidor
`0x3514CA80` lê os mesmos seis bytes. Na captura
`0F03280000000A 0A 08 00 01 00 00 03`, portanto, `08 00 01 00 00 03` não são três `u16`.
O stream acompanha `0x030A` aproximadamente 1:1; o relay valida o tamanho, mas ainda não
correlaciona sequência/cadência.

### `0x0311` — animação normal, ataque ou dano

O dispatcher `rakion.bin:0x00411760` lê `u8 sourceEcho` e chama
`CPlayer::DoAnimPacket(CNetMessage&) @ entitiesmp.dll:0x35152990`. O byte seguinte seleciona uma
união explícita:

| `kind` | corpo após o kind | consumidor |
|---:|---|---|
| `0` | `u8 animationId` | `ExecNormalAnim(long) @ 0x3513E570` |
| `1` | `u8 animationId` | `ExecAttackAnim(long) @ 0x3514A5F0` |
| `2` | `u8 argument0, u8 argument1, u8 argument2` | `ExecDamageAnim(long,long,int) @ 0x3514A6C0` |

Assim, os shapes lógicos medem 10 bytes para normal/ataque e 12 bytes para dano. A captura de 12
bytes com `kind=1` também é aceita porque o receiver consome só o primeiro argumento e tolera os
dois bytes finais. HP e morte não estão nesse pacote. A animação isolada não comprova hit: dano
autoritativo ainda exige correlação com sequência, arma, posição, orientação, janela, alvo e match.

#### Argumentos de `kind=2` medidos entre dois clientes

Captura humano×humano de 28/07/2026 (dois clientes no field 1, seats 0 e 1, quatro mortes,
26 frames `kind=2`) fecha os três argumentos de `ExecDamageAnim`:

| `(arg0, arg1, arg2)` | contexto na captura |
|---|---|
| `(01, 02, 01)` / `(02, 01, 01)` | golpe melê que não derruba, **alternando** entre os dois |
| `(08, 04, 01)` | golpe de outra classe de ataque |
| `(0F, 07, 01)` | golpe que derruba, morte e frame de respawn |
| `(00, 0A, 01)` | dano ambiental periódico (~1,1 s), morte com `cause=2` — não é melê |

`arg2` é `01` em **todos** os 26 frames; não carrega o assento de quem golpeou. A alternância
casa 1:1 com o ataque do agressor: `kind=1 arg=00` → `(01,02)`, `kind=1 arg=01` → `(02,01)`, e o
combo `arg=19,18,0C` → `(0F,07)` seguido de `EPlayerDeath`.

Duas consequências de arquitetura, e são o ponto principal desta seção:

1. **Cada cliente é autoritativo sobre o próprio corpo.** Todo evento de entidade sai com
   `sender == idxA`: a vítima publica sobre si mesma o `PlayerRemainHP 0x0191000C` e, no mesmo
   milissegundo, a reação `0x0311 kind=2`. O atacante publica só `EShootWeapon 0x01910007`.
   `ECollisionState 0x0191002A` é a única exceção — sai sobre a outra entidade.
2. **`EPlayerDamage 0x0191000B` não aparece nenhuma vez** numa partida completa entre dois
   clientes. O evento existe no binário e `ReceiveDamage` o consome, mas o trilho P2P do jogo não
   o usa: o cliente resolve o próprio dano e replica só o resultado. Impor dano a uma entidade
   **remota** por esse evento não desenha reação — é preciso falar o par
   `RemainHP` + `0x0311 kind=2` sobre a própria entidade.

A captura do produtor v258 fecha ainda dois contratos usados pelo bot sintético:

- locomoção remota não nasce do `actionCode` de `0x030A`, que permaneceu zero; ela usa
  `kind=0`, com `01=Stand`, `04=MoveForward`, `0C=Jump` e `0E=Rise`;
- durante `MoveForward`, o vetor de deslocamento observado aponta para `heading wire ± 180°`.
  Portanto, o heading do domínio deve ser invertido no codec; usar o valor wire diretamente também
  inverte o cone frontal de validação de melee;
- a reação de dano observada usa `kind=2` e argumentos `0F 07 <attackerSeat>`. Em particular,
  vítima seat 0 atingida pelo seat 1 produziu `00 02 0F 07 01`, e a direção inversa produziu
  `01 02 0F 07 00`.

A DLL deve chamar `ExecDamageAnim` com esses três argumentos; usar `(1,0,0)` não seleciona a
mesma reação e não derruba o avatar. O contador HIT local usa a função exportada em `0x35153CE0`;
o getter do jogador em `0x352B3630` é uma função direta, não um ponteiro armazenado nesse endereço.

## Controle TCP de morte e saída

### `0x4F` — morte reportada

Cliente para World:

```text
[u16 0x004F][u8 cause][u8 killerSeat]
```

World para jogadores ativos:

```text
[u16 0x004F][u8 victimSeat][u8 cause][u8 killerSeat][u8 score0][u8 score1]
```

O produtor cliente também está fechado no dump runtime. O export
`CPlayer::Death(EPlayerDeath) @ 0x3515E830` recebe o evento de 20 bytes por valor; depois da base
`CEntityEvent` de oito bytes, os três `u32` ficam em `+0x08`, `+0x0C` e `+0x10`. A faixa
`0x3515E8DE..0x3515E9D9` deriva o byte `cause`, reduz o segundo campo a `killerSeat` e chama
`IScavengerWorldNet::SendFieldGameDiePlayer` pelo slot virtual `+0x128`. Não existe call site desse
slot em `rakion_orig.exe`; a única chamada exata desta build está no `entitiesmp.dll`.

| primeiro campo de `EPlayerDeath` | `cause` enviado | derivação comprovada |
|---:|---:|---|
| `1` | `2` ou `8` | predicado `0x35169440`; tráfego gráfico confirmou `8` em eliminação comum, valendo um ponto |
| `3` | `7` ou `4` | `4` somente quando o terceiro campo resolve a classe `NpcGoldGolem`; senão `7` |
| `8` | `1` | substitui o killer pelo próprio seat em `CPlayer+0x264` |
| `4` | `5` ou `6` | segundo campo `0` gera `5`; segundo campo `10` gera `6` |
| demais | `3` | fallback do cliente |

O produtor não emite `cause=0` nessa função. `cause=1` é, portanto, a morte própria comprovada;
o efeito no World também coincide: penaliza a vítima em Deathmatch e pontua o time oposto em Team
Death. O nome histórico do predicado que separa `2/8` e os rótulos originais de `2/3/5/6/7` não
estão preservados: a tabela exportada `EKillType_values` contém apenas `0..6` com nomes vazios.
Esses valores devem ser tratados como categorias wire, sem rótulos inventados.

O handler atual confirma apenas:

- sessão e status `InField`;
- `cause <= 8`;
- `killerSeat <= 0x13`;
- field em jogo, fase `Playing`, vítima em state `4` e ainda não morta.

Depois chama `ApplyReportedDeath`, portado de `FUN_004087D0`. Modos `0/1` marcam eliminação;
Deathmatch e Team Death mantêm state `4` para permitir novos respawns e aplicam o score específico
do modo; Boss encerra quando morre um dos líderes. Não há prova de hit, distância, linha de visão
ou HP zero no World porque essa autoridade é P2P/client-side no v258.

### `0x46` — morte/saída sem killer

O sender informa saída/give up e o World chama `OnPlayerExit` com causa `0`, depois
ecoa o seat. Se não restar vivo, pode encerrar o match. Esse evento precisa permanecer separado de
disconnect de rede, surrender voluntário e morte de gameplay para evitar estatística incorreta.

O byte `flag` não identifica uma vítima. Quando `flag < 2` e o player-record está ativo, o original
recalcula/aplica a penalidade de EXP e monta o comando DB `0x0C` mais a resposta cliente
`0x58 [i32 remainingExp]` antes de `FUN_00407E00`. `FUN_0041B940` põe o primeiro na fila do worker;
`FUN_004138B0` executa `UPDATE CharacterInfo SET exp`, e somente o ACK interno `0x0C` fica sem case
em `FUN_004295C0`. O `.NET` agora persiste o valor de forma assíncrona, publica `0x58` ao sender e
mantém o broadcast final `0x46 [seat]` separado.

## Estado atual de combate

`PlayerRec` separa `RoundScore`, `CounterA`, `CounterB` e `ResultPoints`, além de `State`,
`WeaponState`, `Dead`, `Cause`, `Team` e `Slot`, reproduzindo `+0x12C..+0x130`.
Não contém:

- HP atual/máximo e AP atual/máximo;
- CP atual/máximo e custo de summon;
- posição, velocidade, heading ou última sequência aceita;
- ação, arma real, cooldown e janela de hit;
- atacante/dano confirmado e assistências;
- instante de morte, prazo de respawn e invencibilidade;
- latência, perda, jitter ou estado bad-network.

Os campos `hp`, `ap`, `speed`, `attackspeed` e `maxcp` de `characterinfo` são pontos de atributo
persistentes, não os valores consumíveis do match. Copiá-los diretamente como HP/AP/CP atuais seria
misturar progressão com estado runtime. É necessária uma fórmula por classe/equipamento/buffs.

## HP, AP e CP

`EPlayerRemainHP` possui corpo exato `[u32 playerId][f32 hp][f32 ap]`. A captura
`0C839D...0C0091010C000000000000000000C2420000C242` decodifica player `0`, HP `97.0` e AP
`97.0`. `CPlayer::Send_HP_AP @ 0x35140D30` lê o byte de identidade em `player+0x264`, chama os
getters virtuais de HP/AP e envia o evento com `CEntity::SendEvent(..., 1)`.

As operações locais comprovadas são:

- `AddHP` e `ReduceHP`: leem HP pelo slot virtual `+0x158` e saturam pelo setter `+0x15C`;
- `ReduceAP`: lê AP por `+0x170` e grava por `+0x16C`;
- `WorkReduce_HP_AP`: aplica metade do valor em uma condição de estado (`0.5`), reduz os recursos,
  zera AP ao atravessar zero e publica `EPlayerRemainHP`;
- `SetAlive` limpa o instante de morte; `SetDead` grava o tempo atual uma única vez;
- `EPlayerDamage` tem corpo de 40 bytes tipado: identidade, `DamageType`, `DamageMotionType`, dois
  valores de dano e dois vetores; o codec preserva também o `u16` reservado.

CP é reduzido localmente em `Death` e continua fora do snapshot `EPlayerRemainHP`. Os atributos
persistidos de `characterinfo` continuam sendo pontos de progressão, não HP/AP atuais.

Requisitos mínimos do domínio:

```text
PlayerCombatState
  MaxHp, Hp
  MaxAp, Ap
  MaxCp, Cp
  Alive, LastDamage, LastAttacker
  Position, Heading, LastSequence
  Action, ActionStartedAt, Weapon
  DiedAt, RespawnAt, InvulnerableUntil
  NetworkQuality
```

Valores devem usar tipos com margem para cálculo e saturação explícita; wire estreito só na borda.

## Movimento, velocidade e ação

O servidor atual não guarda posição nem mede deslocamento. Logo não detecta teleport, speed hack,
ação impossível, spam ou ataque durante estado morto. Os contadores zerados no `0x4F` para alguns
modos não substituem validação temporal.

Uma validação compatível deve considerar tolerância de rede e o stat de speed, sem decidir por um
único pacote. O servidor precisa:

1. rejeitar sequência repetida ou regressiva fora da janela de reorder;
2. calcular distância máxima pelo delta de tempo e perfil do personagem;
3. validar transições de ação/arma e cooldown;
4. correlacionar `0x030A`, `0x030F` e `0x0311`;
5. acumular violações e aplicar correção/flag antes de kick automático.

## Respawn, invencibilidade e bad-network

O respawn original também é client/P2P. `RespawnWork @ 0x35164E10` só atua nos modos `2`, `3` e
`4`, exige player morto e compara `tempoAtual - diedAt` com `GetRespawnTime`; ao vencer chama
`Respawn`. Para Deathmatch, Team Death e Boss, `GetRespawnTime` retorna **7 segundos**. Stage
(`0`) retorna `-1`; Golem (`1`) possui ramos de **25, 30 ou 40 segundos**, escolhidos por dois
estados consultados na entidade de partida que ainda não têm nome semântico confirmado.

`Respawn @ 0x35162370` chama `SetAlive`, restaura máximos/HP/AP conforme o modo, publica
`EPlayerRemainHP`, reposiciona/reativa a entidade e o fluxo publica `ERespawn` sem corpo.

O dump runtime de `gamemp.dll` fechou os dois símbolos antes protegidos. A rotina de registro
`0x10013AE0` associa `gam_bRespawnInPlace` ao global `0x10036248`, cujo default runtime é `1`, e
`gam_tmSpawnInvulnerability` ao global `0x10036228`, cujo default é `3.0f`. O único consumidor
não relacionado ao registro é `0x1001D740`, que monta `CSessionProperties`:

```text
CSessionProperties+0x6C = gam_bRespawnInPlace
CSessionProperties+0x98 = gam_tmSpawnInvulnerability
```

`GetGameSpyRulesInfo @ 0x1001D0D0` publica `CSessionProperties+0x6C` como
`\\respawninplace\\%d`; não aplica respawn nem imunidade. Em `entitiesmp.dll`, a IAT
`0x352B3364` resolve `CNetworkLibrary::GetSessionProperties`. Foram encontrados 52 operandos que
referenciam essa API e 25 funções consumidoras recuperadas; nenhuma lê `+0x6C` ou `+0x98`.
`CPlayer::GetRespawnTime`, `RespawnWork`, `Respawn`, `SetAlive` e a trilha de dano também não
consultam esses offsets.

Portanto `1` e `3.0` são defaults de sessão confirmados e o primeiro ainda aparece na consulta de
regras, mas **não há consumidor de gameplay comprovado nesta build**. O backend não deve criar
invulnerabilidade de três segundos nem alterar a posição de respawn só por causa desses símbolos;
isso exigiria captura visual ou um call site adicional ainda inexistente no binário analisado.

O indicador legado de bad ping e seu threshold estão fechados acima, mas o backend ainda não mede
RTT, perda, reorder, jitter nem idade da última atualização. Portanto não deve aplicar kick com base
apenas no flag informado pelo cliente. O sender UDP é resolvido por endpoint autenticado; janela de
ACK, métricas próprias e política de timeout continuam pendentes em
[`../core/udp-p2p-tunneling.md`](../core/udp-p2p-tunneling.md).

## Causas de morte e placar

A derivação nativa acima fecha todos os ramos que alimentam `0x4F`: `1` é morte própria; `4` é
`NpcGoldGolem`; `7` é o outro ramo de entidade; `5/6` distinguem os seats especiais `0/10`; `2/8`
dependem do predicado especial, e os demais caem em `3`. O scoring original continua sendo delta
`0`/penalidade para causa `1`, delta `2` para causa `8` e `1` nos demais casos, variando por modo.

O que permanece sem nome não é o fluxo binário, mas a nomenclatura histórica das categorias. Uma
futura autoridade server-side deve derivar killer/cause do evento validado ou comparar o reporte do
cliente com a trilha recente de dano; disconnect e surrender continuam no fluxo separado `0x46`.

## Falhas e riscos confirmados

| Área | Estado atual | Impacto |
|---|---|---|
| ação peer | allowlist e shapes `0x030A/0x030F/0x0311` | sem correlação de sequência |
| hit/dano | evento `0x830C` validado e relayado | cliente decide impacto, fiel ao original |
| morte | cliente informa killer/cause | score forjável |
| HP/AP | snapshot P2P decodificado | World não mantém espelho autoritativo |
| CP | local no cliente | não aparece em `EPlayerRemainHP` |
| movimento | sem posição no domínio | teleport/speed não detectados |
| respawn | timer e evento P2P mapeados; relay aceita 19 B | não é estado do World |
| invencibilidade | defaults 1/3.0 confirmados | nenhum consumidor encontrado nesta build |
| bad-network | ausente | sem diagnóstico ou política |
| sender UDP | slot/IP/chave e endpoint exato | identidade fechada; conteúdo da ação ainda opaco |

## Implementação compatível e evolução opcional

Para fidelidade ao v258, a arquitetura atual permanece adapter UDP → codec explícito → relay do
field. Uma futura autoridade server-side é outra feature e deve ficar isolada do transporte:

```text
UDP/TCP adapters
  -> ActionStreamAssembler
  -> ActionValidator
  -> CombatSimulation
  -> DamageResolver
  -> Death/RespawnPolicy
  -> GameModeRules
  -> eventos wire + ScoreBoard
```

### Ordem segura

1. Registrar sequência/cadência por peer sobre a identidade UDP já autenticada.
2. Observar `EPlayerDamage/EPlayerRemainHP/EPlayerDeath/ERespawn` sem alterar o wire.
3. Introduzir `PlayerCombatState` apenas em shadow mode, sem competir com a engine.
4. Calcular HP/AP/CP máximos numa única política por classe, stats e equipamento.
5. Validar movimento e transição de ações em modo observação.
6. Implementar hit/dano e comparar com `0x4F` sem bloquear o cliente.
7. Tornar morte server-authoritative por flag, com fallback monitorado.
8. Adicionar respawn, invencibilidade e qualidade de rede por regra de modo.
9. Fazer placar e resultado consumirem somente `DeathConfirmed` idempotente.

Não colocar regras de dano nos handlers. Eles apenas decodificam contratos, autenticam a sessão e
publicam comandos ao domínio.

## Ativação e rollback

```ini
[Combat]
ActionStream=false
MovementValidation=observe
DamageAuthority=client
RespawnPolicy=false
NetworkQuality=false
```

Ativação gradual:

1. `ActionStream=true` em canal de teste, apenas observabilidade;
2. `MovementValidation=observe`, sem kick;
3. comparar dano calculado e mortes reportadas;
4. habilitar `DamageAuthority=server` primeiro em Team Death;
5. ativar respawn/invencibilidade por modo;
6. promover após teste real com dois clientes e perda/reorder simulados.

Rollback volta `DamageAuthority=client` e desliga políticas novas, preservando o relay compatível.
Flags não podem mudar no meio de um match.

## Testes mínimos

- codec e limites de `0x030A`, `0x030F`, `0x0311`, `0x46` e `0x4F`;
- sequência duplicada, reorder, wrap e pacote de sender inválido;
- movimento válido, teleport e tolerância por jitter;
- hit fora de alcance, cooldown, vítima morta e friendly fire;
- absorção AP, saturação HP/AP/CP e morte exatamente uma vez;
- suicide, ambiente, NPC, surrender e disconnect;
- respawn, posição, restauração e fim de invencibilidade;
- bad-network com perda, latência e recuperação;
- dois clientes vendo a mesma vida, morte, respawn e placar;
- replay do mesmo evento sem duplicar score ou resultado.

## Critério de conclusão

O RE compatível do transporte de combate está fechado quando `0x030A/0x030F/0x0311`, o envelope
`0x830C`, os quatro eventos de player, o reporte `0x4F` e os timers acima passam nos codecs e no
relay. Uma meta diferente — impedir cliente adulterado de criar dano/kill — exige simulação
server-authoritative, fórmulas por arma/equipamento e validação visual; não deve ser apresentada
como comportamento original desta build.
