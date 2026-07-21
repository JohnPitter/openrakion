# Engenharia reversa do ciclo de field e partida — Rakion v258

## Veredito

O ciclo competitivo possui agora uma máquina explícita e compatível com `FUN_00409940`: armamento
de 40 segundos, início por spawn, timeout específico por modo, intermissão de 15 segundos,
controle de população, próximo round e fim de match. As transições são determinísticas e cobertas
por testes. Os frames críticos possuem goldens e os probes headless exercitam duas sessões.

O RE está fechado para a máquina estática desta build e a implementação headless está funcional.
Ainda não existe evidência para declarar a jornada gráfica PvP completa: faltam observar com dois
clientes reais spawn, respawn, troca de round, resultado e retorno à sala.

## Fontes canônicas

- `worldserv.exe` v258:
  - `FUN_004065E0`: inicialização de round;
  - `FUN_004079D0`: engage;
  - `FUN_00407BE0`: fim de match e `0x44`;
  - `FUN_00408440`: ready/spawn e `0x48`;
  - `FUN_00409940`: motor `Pre → Playing → RoundEnd`;
- `engine.dll:0x361925B0`: builder C→S `0x50`;
- `Domain/Field.cs`, `Field.Lifecycle.cs` e `Field.Combat.cs`;
- `WorldServer.MatchTick`, handlers `ReconCombatA/B` e `ReconRoomB`;
- `FieldLifecycleRulesTests`, `FieldCombatRulesTests` e `FieldLifecycleFrameGoldenTests`;
- `tools/ghidra/DecompileWorldMatchLifecycle.py`;
- probes `world_room_probe.py`, `world_combat_probe.py`, `world_deathmatch_probe.py` e
  `world_objective_probe.py`.

## Máquina original reconstruída

| Fase | Prazo | Comportamento no vencimento |
|---|---:|---|
| `Pre=0` | 40 s | inicia se há player `state=4`; no solo, remove `state=3` e termina com motivo `1` se ninguém spawnou |
| `Playing=1` | duração + 3 s | competitivo decide timeout por modo, envia `0x4A` e entra em intermissão |
| `RoundEnd=2` | 15 s | incrementa round, valida população e envia apenas `0x49`, ou encerra o match |

`ArmMatch(now)` cria um novo `MatchId`, limpa placares e marca os ocupantes como `state=3`.
`OnPlayerReady` aceita somente registro `state=3` em field ativo e move apenas esse registro para
`state=4`. Na fase `Pre`, inicia quando não resta player aguardando. Em `Playing`, sincroniza o
`0x48` somente ao jogador atrasado; em `RoundEnd`, devolve o `0x4A` vigente. Um `0x48` repetido de
registro já `state=4` é ignorado, como em `FUN_00408440`, sem reiniciar o lifecycle local. Após 40
segundos, o motor também pode iniciar com quem já spawnou, sem promover artificialmente os atrasados.

### Timeout por modo

| Modo | Valores comparados | Efeito |
|---:|---|---|
| `1` Golem | `ObjectivePairA/B` (`+0x2C4/+0x2C6`) | maior valor ganha o round |
| `2` Deathmatch | nenhum placar de time | não altera `Wins0/Wins1` |
| `3` Team Death | `Score0/Score1` | maior placar ganha |
| `4` Boss | `BossTargetA/B` (`+0x2C8/+0x2CA`) | maior valor ganha |

O timeout grava `RoundEndReason=0`. Empate usa `LosingSideWire=2`. Golem, Team Death e Boss
incrementam o `Wins` vencedor; Deathmatch não converte o melhor jogador em vitória de time.

No próximo round, os quatro objetivos voltam a `1`, como no binário. O servidor não promove
players `state=3` para `state=4` e publica somente `0x49`; o envio adicional de `0x48` existente
antes desta auditoria era divergente.

### Encerramento entre rounds

- `Round > MaxRounds`: `EndMatch(2)`;
- Golem, Team Death e Boss sem player ativo em um dos lados: `EndMatch(5)`;
- Deathmatch com menos de dois players ativos: `EndMatch(6)`;
- caso válido: inicia o próximo round.

Player ativo significa registro em `state=3` ou `state=4`, exatamente como a varredura original.

## Frames S→C

| Tipo | Layout lógico | Emissão |
|---:|---|---|
| `0x43` | `[u16 type][u8 status]` | autorização do start |
| `0x44` PvP | `[u16 type][u8 reason]` | fim do match |
| `0x48` | `[u16][round][remaining:u16][wins0][wins1][mvp0][mvp1]` | início após engage/spawn |
| `0x49` | `[u16][round][mvp0][mvp1]` | próximo round |
| `0x4A` | FIELD body `[reason][losingSide][wins0][wins1]` | fim de round |
| `0x4F` | FIELD body `[victim][cause][killer][scoreA][scoreB]` | morte reportada |

O `0x48` tem nove bytes lógicos e zero-padding até o bloco AES. O `0x49` tem cinco bytes lógicos.
O `0x44` longo com nome da sala pertence apenas à ponte visual do stage solo.

## Concorrência e resultado

Toda transição do motor e as mutações críticas de engage, ready, objetivo, morte, Boss e saída
usam `Field.SyncRoot`. Assim, o tick não decide um round enquanto um handler altera o mesmo
placar ou objetivo.

O settlement PvP captura, antes do I/O:

- `MatchId` e `fieldId`;
- vencedor;
- sessão, personagem, seat, pontos e resultado de cada participante.

Há um gate por `MatchId` para impedir duas operações simultâneas. Após o commit, o overlay de
memória é aplicado uma vez aos participantes capturados. `Settled=true` só é escrito se o field
ainda representa o mesmo `MatchId` encerrado; uma revanche iniciada durante o banco não é marcada
por engano. A tabela `match_settlement_ledger` mantém o banco idempotente.

O `0x50` competitivo continua separado: EXP, gold e cells possuem ledger por match/round/personagem
e confirmação somente após commit. Stage solo usa `0x53` e `StageRun`, não o settlement PvP.

## Implementação e ativação

Não existe a flag `MATCH_ENGINE_V2`; ela não deve ser adicionada apenas para ativar este caminho.
O motor é iniciado pelo `WorldServer` e atua nos fields em `State=2`.

1. Compile a solução com o SDK .NET disponível no `PATH`.
2. Inicie o stack pelo `server/RakionServer/start-stack.ps1`.
3. Crie uma sala Battle, deixe todos os membros ready e inicie pelo host.
4. O start chama `ArmMatch`; no cliente gráfico v258, o `0x48` de carregamento pode chegar antes
   do `0x4B` que publica o spawn. O spawn preserva o assento que já está em `playing`. O World
   aguarda todos os assentos em `spawning` e publica um único `0x48` sincronizado para a sala;
   o loop global chama
   `AdvanceLifecycle`.
5. Confira logs `field` para início, timeout, próximo round, fim e settlement.

Não há migração manual exclusiva desta máquina. Os schemas de ledger são criados pelo boot e os
smoke tests MySQL verificam idempotência e rollback.

## Validação automatizada

- `FieldLifecycleRulesTests`: prazos, todos os modos, empate, população, motivos `1/2/5/6`,
  objetivos em `1` e não promoção de atrasados;
- `FieldCombatRulesTests`: frag, morte, líder, objetivo e scoring;
- `FieldLifecycleFrameGoldenTests`: bytes de `0x44/0x45/0x46/0x48/0x49/0x4A/0x4F`;
- probes com duas sessões: entrada, inclusive o primeiro `0x4B` no relay canônico, relays
  subsequentes, Team Death, Golem/Boss, saída e fim curto.

## Limites ainda abertos

- observação gráfica PvP com dois clientes;
- ciclo visual de respawn;
- efeito visual dos motivos `2`, `5` e `6` no cliente;
- reconexão à partida em andamento — não há contrato reconstruído que autorize inventá-la;
- valores de morte, objetivo e recompensa continuam reportados pelo cliente, como no v258; tornar
  o servidor autoritativo seria uma evolução de segurança, não requisito para fidelidade do RE.

## Critério de conclusão visual

Registrar captura com dois clientes contendo: start, `0x48`, morte, respawn, timeout ou objetivo,
`0x4A`, `0x49`, último round, `0x44`, resultado persistido e ambos de volta à sala. Até essa prova,
o status correto é “RE estático e headless fechado; integração gráfica pendente”.
