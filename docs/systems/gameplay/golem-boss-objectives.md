# Engenharia reversa de Golem, Golden Sword e Boss — Rakion v258

## Escopo e veredito

Este documento separa duas camadas que antes estavam misturadas:

- a autoridade fiel do `worldserv.exe`, reconstruída no World .NET;
- a simulação client/P2P de entidades, Golden Sword, dano e efeitos visuais.

**Veredito:** os contratos do World para Golem/Boss estão fechados e implementados. O World
original não mantém entidades nem calcula HP: recebe o par final do Master Golem em `0x4D`, encerra
o round quando um valor zera e, no modo Boss, guarda dois valores reportados pelos líderes em
`0x60`. A jornada visual das entidades continua no cliente/game session e ainda requer validação
gráfica com dois clientes. Tornar tudo server-authoritative é hardening opcional, não requisito de
fidelidade ao World v258.

## Evidência reproduzível

- `tools/ghidra/DecompileWorldGolemBoss.py` decompila os handlers e helpers originais;
- `FUN_00424980` / opcode `0x4D` chama `FUN_00405D70`;
- `FUN_00425CC0` / opcode `0x60` chama `FUN_00405EF0`;
- `tools/world_objective_probe.py` valida Golem e Boss com duas sessões;
- assets `MasterGolem.ecl`, `GoldGolem.ecl`, `BG_GoldSwordMode_Begin.mp3` e
  `BG_GoldSwordMode.mp3` confirmam a apresentação client-side;
- `DecompileClientEntitySync.py` fecha os consumers/builders `0x0308/0x030B` e a resposta
  de reparo `0x0310` no `engine.dll`.
- `DecompileEngineGolemObjective.py` fecha ownership local/remoto, Gold Golem e late join;
- `DecompileClientGolemObjective.py` e `FindClientGolemEventConsumers.py` fecham os eventos de
  Golden Sword, respawn/rebirth e Master Golem em `entitiesmp.dll`.

## World `0x4D` — Master Golem

Request C→S:

```text
opcode 0x4D
s16 first
s16 second
```

Regras exatas de `FUN_00405D70`:

1. aceita somente `field.state == 2` e fase `Playing`;
2. grava `first` em `field+0x2C4` e `second` em `field+0x2C6`;
3. não exige master, host ou líder: qualquer membro válido do field pode reportar;
4. `first == 0` dá a vitória ao time 1;
5. caso contrário, `second == 0` dá a vitória ao time 0;
6. valores não-zero são guardados, mas não encerram o round;
7. no fim, define motivo `2`, fase `RoundEnd` e deadline de 15 segundos.

Response S→C para cada player em estado `4`:

```text
0x4A [reason=2] [losingSide] [wins0] [wins1]
```

O offset `field+0x2BF` não representa o vencedor. Ele codifica o **lado perdedor no fio**:

- `0`: time 1 venceu;
- `1`: time 0 venceu;
- `2`: empate nos fluxos que o admitem.

Essa inversão explicava a aparente inconsistência da implementação antiga. O estado agora usa os
nomes `RoundEndReason` e `LosingSideWire`, e todos os emissores usam `Field.Build0x4a()`.

## World `0x60` — alvo/valor do Boss

Request C→S:

```text
opcode 0x60
u8 targetGroup
u16 value
```

Regras exatas de `FUN_00405EF0`:

- field em jogo, fase `Playing` e modo `4`;
- reporter precisa ser `LeaderSlotA` ou `LeaderSlotB`;
- `targetGroup < 10` grava `value` em `field+0x2C8`;
- demais valores gravam em `field+0x2CA`;
- não calcula HP, não decide o round e não envia resposta ou broadcast.

O nome exportado `SendFieldGameMasterBossHP` não autoriza interpretar `value` como HP
server-side. A implementação antiga guardava por sessão e inventava um broadcast; ambos foram
removidos. O estado atual fica no próprio `Field` como `BossTargetA/B`.

## Morte e vitória do Boss

No modo `4`, `FUN_004087D0` encerra o round quando a vítima do `0x4F` é um dos líderes em
`field+0x122/+0x123`. A vitória vai para o time oposto. O World legado confia no reporte de morte
do cliente; HP, buffs e combate do Boss não são calculados pelo servidor.

`FUN_00408440` escolhe esses líderes no início de cada round Boss pelo maior nível de personagem
(`user+0x1531`) de cada time. A comparação é estrita: em empate, permanece o primeiro seat
encontrado. Os offsets usam `0x14` como sentinela de ausência e não representam MVP recalculado
por score. Fora do modo Boss, ambos permanecem em `0x14`.

## Entidades e Golden Sword

Gold Golem, Golden Sword, Master Golems, placement, pickup/drop, energia visual e snapshots de
late join pertencem ao game session/client P2P. A ausência desses objetos no domínio do World .NET
é coerente com o binário original analisado. Eles só devem ser adicionados ao backend em um modo
de autoridade moderna explicitamente incompatível.

### Contratos client/P2P fechados

`SendInfoCreateMasterGolemTo @ 0x3610B1E0` envia uma mensagem reliable `0x0308`:

```text
u8  source/hostSlot
u8  teamIndex
u16 entityField
6 x 4 bytes de placement
blob de init serializado
```

`CSessionState::HandleMessage @ 0x3610D7C0` consome `0x030B` como:

```text
u16 timingOrState
u8  entityKind       2=general NPC, 3=map NPC, 4=Master Golem
u8  groupIndex
u8  entityIndex
s16 x
s16 y
s16 z
s16 heading
```

Se o kind `4` ainda não existe, o cliente cria o par de Master Golems. Se o kind `3` referencia
um map NPC ausente, ele envia reliable `0x0310` para solicitar/reparar esse estado. Uma leitura
anterior que atribuía `0x030B` a essa resposta estava incorreta. O corpo reliable e seus ACKs
trafegam direto entre clientes; o World publica endpoints e oferece fallback, não deve sintetizar
`0x0308`.

### Golden Sword e Gold Golem

O evento `EGoldSword` tem id `0x044D000B`, tamanho total `0x0C` e dois bytes úteis após o cabeçalho
base do evento:

```text
u8 enabled
u8 secondary
u16 padding
```

`CNpcBase::SetGoldSwordModeForPlayer` resolve o player-alvo pelo contexto do evento. Quando
`enabled != 0`, ele lê a propriedade de modo do player, registra um marcador em `CNpcBase+0x38A0`
quando `secondary == 0`, reinicia portadores anteriores e chama `CPlayer::AquireGoldSword`.
Quando desabilitado, chama `CPlayer::RestoreGoldSword` e zera o marcador.

`AquireGoldSword` grava `1` e `RestoreGoldSword` grava `0` na propriedade `CPlayer+0x2B98`.
`ResetGoldSwordForAllPlayers` percorre os 20 slots, seleciona players em estado `3` e restaura
qualquer portador. `CPlayer::RespawnPlayers` também restaura a Golden Sword, portanto respawn não
preserva o porte.

`CPlayer::ChangeModeGoldSword` toca
`SoundsSV\\BG\\BG_GoldSwordMode_Begin.mp3`, dispara a transição visual e aplica o modo aos players
elegíveis da sessão. `FieldInfo::SetGoldSwordMode(int)` armazena o modo em `FieldInfo+0x48A4`.

Eventos auxiliares comprovados:

| Evento | ID | Payload próprio |
|---|---:|---|
| `EGoldGolemRespawn` | `0x04690000` | vazio |
| `EGoldGolemRebirth` | `0x04690001` | vazio |
| `EMasterGolemRespawn` | `0x04650000` | vazio |
| `EMasterGolemDamage` | `0x044D0015` | dois `u32` ainda sem nomes seguros |

`CSessionState::GetGoldGolem` procura até 65 map NPCs pela classe `NpcGoldGolem`. O Gold Golem é,
portanto, entidade da game session, não objeto do World server.

### Ownership e late join

`CSessionState::SetMasterClient(bool)` cria os Master Golems ausentes quando o cliente se torna
master, alterna Master Golems e map NPCs entre `ELocalEntity`/`ERemoteEntity` e reconstrói o Gold
Golem a partir do map NPC quando necessário.

`CSessionState::AddRemotePlayer` fecha o snapshot de late join. Somente o client master envia ao
novo peer, após adicionar o player:

1. `SendInfoCreateNpcTo` (`0x0307`);
2. `SendInfoCreateMapNpcTo` (`0x0309`);
3. `SendInfoCreateMasterGolemTo` (`0x0308`), para os dois times;
4. `SendInfoMapItemStatus` (`0x0312`).

O `0x0308` cruza a rede como `0x8308` e seu corpo é o layout já mostrado acima. O contrato de late
join está fechado estaticamente; falta comprovar sua apresentação gráfica.

Ainda falta validar visualmente e em captura P2P direta:

- spawn e atualização `0x0308/0x030B` dos Master Golems com dois clientes;
- ciclo Gold Golem → Golden Sword → portador → drop/retorno;
- energia exibida e áudio de transição;
- seleção/apresentação do Boss e respawn;
- snapshot de late join/reconnect, cujo contrato estático já foi fechado.

## Implementação atual

- `Field.ApplyObjectivePair` replica `FUN_00405D70`;
- `Field.ApplyBossTarget` replica `FUN_00405EF0`;
- `Field.SelectBossLeaders` replica a seleção por nível de `FUN_00408440`;
- `Field.CompleteTeamRound` centraliza decisão, wins, contadores e timer;
- `WorldHandlers.ReconCombatB.Op_0x4D_Recon` publica o `0x4A` canônico;
- `Op_FieldBossTargetReport` usa o seat local real e não transmite `0x60`;
- o dispatch em partida permite `0x60` alcançar o handler reconstruído.

Não há flag para “ativar”: salas nos modos `1` e `4` usam essas regras automaticamente. Para
teste local, suba o World com `server/RakionServer/deploy/worldserver.ini` e execute:

```powershell
python tools\world_objective_probe.py 40708
```

A sonda requer `test` e uma fixture descartável `test2`.

## Validação concluída

- testes de domínio, frames e probes Python verdes;
- `first=0` produz exatamente `0x4A [2,0,0,1]` para os dois clientes;
- `second=0` produz `0x4A [2,1,1,0]` em teste de domínio;
- par não-zero é armazenado sem encerrar o round;
- `0x60` aceita líderes no modo Boss e rejeita não-líder/modo incorreto;
- a sonda comprova que `0x60` não é transmitido e que o relay seguinte continua funcional;
- a eliminação Golem publica `0x4F [0,0,10,10,10]` e `0x4A [1,0,0,1]`;
- a morte do líder Boss publica `0x4F [0,0,10,0,0]` e `0x4A [1,0,0,1]`;
- a sonda extrai o `fieldId` real da resposta de criação e pode ser repetida sem reiniciar o World.

## Riscos legados e hardening opcional

- qualquer membro do field pode forjar `0x4D`, como no original;
- líderes podem reportar valores arbitrários em `0x60`;
- morte, killer e causa vêm do cliente;
- payloads P2P permanecem opacos para o World.

Um modo server-authoritative exigiria entidades, ownership da Golden Sword, validação de dano,
snapshots, anti-replay e decisão transacional. Isso deve ser uma opção separada, porque altera o
contrato e o comportamento histórico.

## Critério de conclusão restante

Para o **RE estático**, World, eventos Golden Sword, ownership e late join estão fechados. Para
declarar a experiência completa do jogo, ainda falta executar duas partidas gráficas e registrar
spawn, pickup/drop, áudio, barras de energia/HP, Boss e late join no cliente original.
