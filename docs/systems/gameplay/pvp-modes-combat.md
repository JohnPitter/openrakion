# Engenharia reversa dos modos PvP e combate — Rakion v258

## Escopo e estado atual

Este documento cobre Golem, Deathmatch, Team Death e Boss no World: criação da sala, times,
ready/start, spawn, morte, give-up, placar, objetivo e vitória. O ciclo de partida está em
[`../core/field-match-lifecycle.md`](../core/field-match-lifecycle.md) e o transporte em
[`../core/udp-p2p-tunneling.md`](../core/udp-p2p-tunneling.md).

**Estado:** os contratos de autoridade do `worldserv.exe` v258 para os quatro modos estão
implementados e validados headless com duas sessões. O World não simula hit, HP, AP ou CP: os
clientes trocam as ações por P2P e o cliente morto reporta `0x4F`. Permanecem pendentes a
validação gráfica dos quatro modos e a nomeação interna de parte dos blobs de ação P2P.

Isso não torna o combate seguro contra cliente modificado. Autoridade server-side seria uma
extensão moderna e incompatível, não uma exigência de fidelidade ao servidor original.

## Fontes reproduzíveis

- `tools/ghidra/DecompileWorldCombatAuthority.py`;
- `tools/ghidra/DecompileWorldMatchLifecycle.py`;
- `tools/ghidra/DecompileWorldGolemBoss.py`;
- `tools/world_combat_probe.py` para Team Death e give-up;
- `tools/world_deathmatch_probe.py` para frag individual;
- `tools/world_objective_probe.py` para Golem e Boss;
- testes `FieldCombatRulesTests`, `FieldLifecycleRulesTests`,
  `FieldRoomOperationsTests` e `FieldLifecycleFrameGoldenTests`.

As funções principais são `FUN_004075A0` (troca de time), `FUN_00407E00` (saída),
`FUN_00408440` (líderes Boss), `FUN_004087D0` (morte), `FUN_00409940` (motor da partida) e
`FUN_0040B900` (penalidade de EXP ao sair).

## Identificação dos modos

| ID | Modo | Regra decisiva no World |
|---:|---|---|
| `1` | Golem | eliminação ou `0x4D` zerando um dos objetivos |
| `2` | Deathmatch | maior score individual ou frag limit |
| `3` | Team Death | score agregado por time ou time vazio |
| `4` | Boss | morte do líder de um dos times |

Modo `0` é usado pela compatibilidade de stage solo e não pertence ao PvP competitivo.

## Sala, seats e times

- o field possui 20 registros, `0..9` para o time 0 e `10..19` para o time 1;
- `FUN_004075A0` procura o bloco oposto inteiro. Por isso `0x3E` também pode selecionar os
  seats `8/9/18/19` quando os anteriores estão ocupados;
- o lock de slot `0x42` tem regra diferente: rejeita especificamente `8/9/18/19`;
- `ForceChangeTeam` aceita target menor que 18, mas a busca de destino continua usando o bloco
  oposto inteiro;
- o master é `MasterSlot`; ao sair do combate, o original prefere outro registro em estado 4 e
  depois estado 3;
- a capacidade publicada da sala competitiva atual é 12, independente do tamanho físico do
  array de registros.

Essa modelagem por blocos também existe no Deathmatch. Embora cada seat possua um `Team`
derivado, o vencedor do modo 2 é calculado pelo score individual, não pelo bloco do seat.

## Ready, start e spawn

- no lobby, `0x3D` altera o ready do membro;
- durante o jogo, o mesmo opcode representa troca de arma;
- somente o master inicia com `0x43`;
- o adaptador de sala exige todos os membros prontos antes do start;
- `0x4B` confirma a entrada individual no stage;
- quando todos os registros esperados chegam, o World publica `0x48` e inicia o round;
- `0x45 [seat]` publica spawn e zera o score individual daquele registro.

Esses contratos foram exercitados com duas sessões. O ready não deve ser novamente misturado
com weapon state nem com o lock de slot `0x42`.

## Autoridade e reporte de morte

O World original não calcula cada hit. Ele recebe do próprio cliente morto:

```text
C -> S  0x4F [cause] [killerSeat]
S -> C  0x4F [victimSeat] [cause] [killerSeat] [scoreA] [scoreB]
```

Os campos do registro usados no resultado são:

- `+0x12C`: score individual do round;
- `+0x12D/+0x12E`: contadores dos lados;
- `+0x130`: pontos de resultado/EXP.

Os offsets do usuário `+0x1531/+0x1534` são nível e EXP, não cash. `FUN_0040B900` desconta da
EXP, ao sair, `(level >> 1) + differential * 5`. A implementação usa `CharLevel` e `CharExp` e
não mantém aliases duplicados com semântica incorreta.

## Regras por modo

### Golem

- uma morte marca a vítima como morta;
- quando um time fica sem jogadores vivos, o outro vence o round;
- `0x4D [s16 objectiveA][s16 objectiveB]` grava o par no field;
- `objectiveA == 0` dá a vitória ao time 1; `objectiveB == 0`, ao time 0;
- objetivo encerra com `0x4A [reason=2][losingSide][wins0][wins1]`.

O probe comprovou tanto `0x4D -> 0x4A` quanto `0x4F -> 0x4A` por eliminação nas duas sessões.
Gold Golem, Golden Sword e Master Golem são entidades da game session/P2P; consulte
[`golem-boss-objectives.md`](golem-boss-objectives.md).

### Deathmatch

- `cause=1` decrementa o score da vítima, sem ficar negativo;
- toda eliminação adversária soma um ao killer, inclusive `cause=8`;
- a vítima não é eliminada permanentemente;
- frag limit encerra o round sem incrementar `Wins0/Wins1`;
- no prazo, empate no maior score retorna lado `2`;
- o resultado W/L/D do World original é draw para modo 2.

O produtor nativo em `entitiesmp.dll:CPlayer::Death @ 0x3515E830` confirma que `cause=1` é morte
própria: ele troca o killer pelo seat local em `CPlayer+0x264`. `cause=8` é um ramo do mesmo grupo
que produz `cause=2`; o predicado distingue os dois, mas seu nome original não sobreviveu no
binário. Em tráfego gráfico real, uma eliminação comum chegou como `cause=8`. Por compatibilidade
com o cliente e com a regra oficial de um ponto por adversário, ambos valem uma kill. A seleção
completa está em
[`combat-actions-status.md`](combat-actions-status.md#causas-de-morte-e-placar).

A regra é publicada tanto pela [Softnyx](https://rakion.softnyx.com/GameInfo/Mode/DeathMathTeam.aspx)
quanto pela [Lemon8/Neosonyx](https://rakion.playlemon8.com/GameInfo/Mode/DeathMathTeam.aspx).
O guia oficial da [Rakion SEA](https://rakionsea.playpark.com/game-guide/game-mode/) também fixa
uma eliminação de jogador em `01 Exp`. Esse EXP integra o resultado `0x50` enviado pelo cliente no
fim do round e não deve ser somado novamente ao processar a morte `0x4F`.

O probe alcançou score individual `0/14` e recebeu `0x4A [1,0,0,0]`, sem inventar vitória do
time associado ao seat.

### Team Death

- kills são agregadas em `Score0/Score1`;
- suicídio e `cause=8` pontuam o time oposto em um ponto;
- kill normal pontua o time do killer;
- frag limit, prazo ou ausência de membros ativos de um time encerram o round;
- o settle grava win/lose/draw pelo time do registro.

O probe histórico confirmou os dois valores emitidos pela reconstrução inicial. A validação gráfica
posterior corrigiu `cause=8` para `+1`; o give-up do último membro do time 0 publica exatamente
`0x46 [seat]` e `0x4A [1,0,0,1]` ao peer ainda ativo.

### Boss

`FUN_00408440` escolhe os líderes somente no início do round Boss:

1. percorre os seats em ordem;
2. considera registros em estado 4;
3. escolhe, em cada time, o maior `CharLevel` (`user+0x1531`);
4. usa comparação estrita `>`; empate preserva o primeiro seat;
5. sem líder, serializa o sentinela original `0x14`.

Não existe recalculo de “MVP por score”. Fora do Boss, `field+0x122/+0x123` continuam em
`0x14`. `0x60 [targetGroup][u16 value]` só é aceito de um líder, atualiza `+0x2C8/+0x2CA` e não
gera broadcast. A morte de um líder em `0x4F` dá o round ao time oposto. Ambos os fluxos passaram
no probe de duas sessões.

## Saída e give-up (`0x46`)

`FUN_00407E00` muda o próprio sender para estado 1, publica a saída e aplica a consequência do
modo:

- Golem: reavalia eliminação;
- Deathmatch: encerra se restarem menos de dois ativos;
- Team Death: encerra se um time ficar vazio;
- Boss: a saída de um líder concede o round ao time oposto.

Uma desconexão real remove o registro (estado 0), mas usa a mesma regra de encerramento. Essa
distinção evita perder o seat de quem deu give-up e evita que o master seja reatribuído ao próprio
jogador que acabou de sair.

## Presença de cliente em tunneling: `0x54/0x55`

No `0x45 FieldGameEnter`, `FUN_004066C0` não calcula MVP. Ela lê `user+0x1478`
(`UsesTunneling`), ativa
`field+0x2CC` (`HasTunnelingClient`) e publica `0x54`. Se o agregado já estiver ativo, um jogador
que entra recebe `0x54` individualmente. `FUN_004067C0` recompõe o agregado e publica `0x55`
quando o último usuário em tunneling sai.

O cliente confirma a semântica: o dispatcher do `engine.dll` chama os callbacks do
`rakion.bin` `FUN_00472F00/00472F10`, que executam
`IScavengerWorldNet::SetHaveTunnelingClient(1/0)`. O roster leva o flag individual
`user+0x1478` logo após os dois nomes; `IsTunneling_Client` lê a cópia em
`session+0x1D6+seat*0x378`. No servidor `.NET`, a ausência de endpoint UDP observado na entrada
da sala é a fonte do flag individual e o `0x45` recompõe o agregado. A presença é removida tanto
no give-up `0x46` quanto na remoção real da sala.

## Cobertura e limites

| Área | Estado |
|---|---|
| IDs 1–4 e criação | confirmado |
| ready/start/spawn | implementado e headless com duas sessões |
| troca de time e blocos 10+10 | confirmado por binário e testes |
| `0x4F` e scoring dos quatro modos | implementado e headless |
| `0x46` e consequência por modo | implementado; Team Death headless |
| presença tunneling `0x54/0x55` | semântica fechada, implementada e coberta por testes |
| Golem `0x4D` e eliminação | implementado e headless |
| Boss `0x60`, líderes e morte decisiva | implementado e headless |
| timeout/intermissão/próximo round | implementado e headless |
| settlement idempotente | implementado e testes de banco |
| ações internas P2P | `0x030A` fechado até `pa_aViewRotation`; arma/disparo/hold fechados por layout e consumidor, com words sem nome histórico mantidos neutros |
| placar, respawn, entidades e efeitos visuais | validação gráfica pendente |

Headless significa sockets reais contra Broker/World/Buddy e MariaDB local, não dois clientes
gráficos. Nenhum resultado desta auditoria substitui a captura visual.

## Segurança

- killer, cause e objetivo são client-authoritative no protocolo legado;
- o UDP autentica sender e field, mas não comprova dano;
- score em byte mantém a aritmética do original e pode fazer wrap;
- resultados econômicos e W/L/D são idempotentes, mas ações de kill não possuem anti-replay
  moderno;
- validação de alcance, velocidade, cooldown e hitbox exigiria um modo server-authoritative.

## Ativação

Não há feature flag PvP. Criar uma sala com modo `1..4` seleciona automaticamente a regra. Para
publicação, uma allowlist operacional por canal pode bloquear modos sem alterar o wire.

Validação mínima antes de liberar:

```powershell
python tools\world_combat_probe.py 40708
python tools\world_deathmatch_probe.py 40708
python tools\world_objective_probe.py 40708
```

As contas `test` e `test2` devem existir e o segundo personagem usado pelos probes é `9001`.

## Critério de conclusão visual

O RE do World está fechado no limite de autoridade observado. O sistema PvP completo do jogo só
pode ser marcado como validado visualmente após executar, com dois clientes gráficos, os quatro
modos, troca de time, ready/start, respawn, placar, objetivo, saída, fim de round e retorno à sala.
