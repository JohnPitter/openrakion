# Rakion v258 — canal e lobby

## Estado da reconstrução

O agregado de canal social, sua presença, chat, saída e as duas famílias de ping estão
reconstruídos estaticamente no `worldserv.exe` v258 e implementados no World .NET. A implementação
agora mantém slots locais `0..99`, serializa todos os membros no `0x1E`, publica entrada `0x1F` e
saída `0x20`, usa nome, classe, substatus e clã reais e remove a presença no disconnect. O header
`0x1E` também preserva o owner sentinel `100`; canais com owner gerenciado transferem-no por
`S→C 0x28` quando o owner sai.

Validação em 2026-07-16: build sem warnings, 471 testes World aprovados e
`tools/world_channel_probe.py` verde contra World/MariaDB reais. A sonda autenticou nove sessões e
comprovou entrada incremental, owner sentinel, nome, snapshot completo, refresh limitado a oito,
chat e saída. Continua
pendente a confirmação visual com dois clientes reais; portanto, o wire e o comportamento headless
estão fechados, mas a renderização não deve ser declarada validada.

## Dois níveis diferentes

O original não usa uma única entidade para tudo que a UI chama de canal:

1. **grupo/IDC externo** — lista em `World+0x60/0x64/0x68`, validada pelo request interno `0x01`;
2. **canal social** — array em `World+0xD8/0xDC`, cada entrada com `0x358` bytes e até 100 membros.

O servidor modela isso separadamente como `WorldGroup` e `Channel`. `ClientSession.GroupId`
representa o primeiro nível; `ChannelId` e `ChannelSlot` representam o segundo.

## Agregado original de canal social

Offsets confirmados:

| Offset | Campo |
|---:|---|
| `+0x00` | tipo do canal |
| `+0x01` | ativo |
| `+0x02` | nome C-string |
| `+0x2B` | senha C-string |
| `+0x34` | capacidade |
| `+0x35` | quantidade atual |
| `+0x36` | slot local do boss/owner |
| `+0x38` | 100 entradas de 8 bytes: `u16 sessionSlot` e flag ocupada |

`FUN_00404FC0` valida entrada e produz estes status no `0x1F`:

| Status | Significado |
|---:|---|
| `0` | sucesso |
| `1` | canal inativo |
| `2` | canal cheio |
| `3` | senha incorreta |

Na entrada automática, `FUN_0041B8B0` percorre os canais até encontrar vaga e grava em
`user+0x148C/+0x148D` o índice do canal e o slot local. O servidor .NET preserva esse comportamento
para o canal padrão ownerless.

O byte `+0x36` usa `100` como sentinel sem owner. Isso explica a captura histórica
`... 00 01 64 63 68 61 6E 6E 65 6C 30 31 ...`: `0x64` não é a letra inicial de
`dchannel01`; é o owner sentinel, seguido do nome real `channel01`.

## Registro de presença

`FUN_0040AFB0` fecha o registro variável:

```text
[characterName\0][u8 class][u8 subStatus][u32 clanId]
```

O último campo é `user+0x14D0`, preenchido pelo bloco de clã (`FUN_0040AA80`) e carregado de
`usergameinfo.clanid`. Ele não é token, ponteiro nem ID do personagem.

### Uso do clã na UI do canal

`engine.dll:0x361932A0/0x361933F0` materializa cada membro numa estrutura alinhada de `0x24`
bytes; `clanId` fica em `+0x14`. Os callbacks de `rakion.bin` passam a estrutura a
`FUN_0040CBF0/FUN_0040CC40`, que escolhem a cor `0x8080C0FF` quando o ID é zero e
`0xFFFF80FF` quando é diferente de zero.

`FUN_0043F700` guarda os nove dwords a partir de `this+0x180`, o que coloca o ID em
`this+0x194`. O draw virtual `FUN_0043FEE0`, porém, só lê o nome em `this+0x184` e a cor já
calculada em `this+0x17C`. Portanto, este fluxo sinaliza “possui clã” pela cor do nome; não resolve
nome, tag ou brasão. A ordem de canais do literal de cor ainda depende de validação visual.

Esse estado também não é cache para a sala: `rakion.bin:FUN_0047A370` destrói o componente ativo
em `DAT_004FEED0+0x17C` antes de instanciar a tela de sala, enquanto `0x37/0x38` constroem outro
modelo de jogadores sem `clanId`.

O código anterior truncava o nome em dois bytes, fixava classe `1`, zerava o clã e listava somente
o próprio jogador. Esses quatro desvios foram removidos.

## Frames World para cliente

### `0x1F` — entrada de membro

```text
[u16 0x1F][u8 status=0][u8 channelSlot][u16 sessionSlot][presence]
```

O sucesso tem `6 + tamanho(presence)` bytes lógicos. `FUN_00404EF0` transmite a entrada a todos os
membros cujo status é `2`, incluindo o recém-chegado.

### `0x1E` — snapshot de membros

```text
[u16 0x1E][u8 type][u8 count]
[u8 ownerSlot][channelName\0][password\0]
count * [u8 channelSlot][u16 sessionSlot][presence]
```

Na entrada, `FUN_00404FC0` envia todos os membros ao novo usuário. No request C→S `0x1E`,
`FUN_00429230` chama `FUN_00404DA0`, que escolhe um ponto inicial aleatório e devolve até oito
membros. `engine.dll:0x361932A0` lê explicitamente `type`, `count`, `ownerSlot`, as duas strings e
os registros nessa ordem. O caso solo capturado continua tendo 28 bytes lógicos e os mesmos bytes
do golden anterior, agora com a interpretação correta de `0x64 + "channel01"`.

### `0x20` — saída

```text
[u16 0x20][u8 channelSlot]
```

`FUN_00405240` libera o slot, decrementa a contagem e transmite a saída aos membros restantes. No
servidor .NET a mesma remoção é idempotente e também ocorre em disconnect abrupto.

### `0x28` — troca de owner

```text
[u16 0x28][u8 newOwnerChannelSlot]
```

Se quem saiu ocupava `channel+0x36`, `FUN_00405240` escolhe o primeiro slot ocupado e chama
`FUN_004051F0`. A rotina grava o novo slot e transmite `S→C 0x28`; o consumidor
`engine.dll:0x361935F0` lê exatamente um byte e o encaminha ao callback `+0x1C4`.

O canal padrão capturado é ownerless (`ownerSlot=100`), portanto não emite `0x28` em saídas. O
domínio .NET suporta a política gerenciada para canais futuros, mas ela não é ativada no bootstrap.

### `0x22` — chat do canal

```text
[u16 0x22][u8 senderChannelSlot][text\0]
```

`FUN_0041BCA0` só atua com status `2`, limita o texto transmitido a 128 bytes e usa o agregado de
canal. O servidor aplica a moderação existente e não usa mais o fallback global `Room(0)`.

O comando `Nome: /roominfo <id>` é interceptado antes da moderação e do broadcast. ID negativo ou
fora de `MaxField` é consumido sem resposta. Para um ID válido, inclusive um slot livre,
`FUN_00406B10` envia diretamente ao solicitante 26 mensagens `0x22`: seis cabeçalhos do field e uma
linha para cada um dos vinte slots. Essas respostas usam `senderChannelSlot=0` e, diferentemente do
chat normal, não incluem terminador NUL no comprimento lógico:

```text
ID[id] Status[state]
Char[creator] Title[room]
Password[password]
Level[min~max] Basic[levelRangeCode] Map[map] Mode[mode]
Boss[masterSeat] Tunneling[flag]
OnVote[active] VotePos[penaltyIndex] BanSlot[targetSeat]
Slot[i] ID[userSlot] Status[state] Auth[substatus] Vote[vote]
```

A última linha é repetida para `i=0..19`. Os rótulos `VotePos` e `BanSlot` são históricos: os
offsets provados são `field+0x2D1` (índice da tabela de penalidade) e `field+0x2D2` (seat alvo).

## Direções e exports legados

O mesmo número pode ter suporte diferente em cada direção. Em particular, `S→C 0x28` é uma
notificação válida. Já `C→S 0x28` é roteado para o commit de enchant `FUN_0041DE40`, não para
`SendChannelChangeBoss`; portanto, o export homônimo existe na ABI, mas não representa uma
operação de canal aceita por este World.

Consumidores S→C confirmados no dispatcher `engine.dll:0x36197320`:

| Op | Consumidor | Corpo principal |
|---:|---|---|
| `1D` | `0x361931E0` | lista de canais |
| `1E` | `0x361932A0` | snapshot com owner, nome, senha e membros |
| `1F` | `0x361933F0` | status/entrada |
| `20` | `0x36193490` | slot que saiu |
| `21` | `0x361934B0` | criação/status |
| `22` | `0x361934E0` | slot e texto |
| `25..27` | `0x36193550..0x361935D0` | nome, senha e capacidade |
| `28` | `0x361935F0` | novo owner slot |
| `29/2A` | `0x36193610/0x36193630` | id/slot e tick |

Não há cases S→C `0x23/0x24` nesse dispatcher.

Requests C→S da mesma faixa:

| Op | Export | Estado no World v258 |
|---:|---|---|
| `1D` | `SendChannelList` | evento UI `0x174` envia `[primeiroId][1]`; dispatcher rejeita com `DISC 0xC9` |
| `1E` | `SendChannelCharacters` | aceito; amostra de até 8 membros |
| `1F` | `SendChannelEnter` | rejeitado com `DISC 0xC9` |
| `20` | `SendChannelExit` | aceito; remove canal e limpa field |
| `21` | `SendChannelCreate` | rejeitado com `DISC 0xC9` |
| `22` | `SendChannelChat` | aceito |
| `23..27` | administração do canal | rejeitados com `DISC 0xC9` |
| `28` | `SendChannelChangeBoss` | não é gestão de canal nesta build: colide com o commit de enchant de oito bytes; `S→C 0x28` continua sendo a notificação de owner |

Esses exports não autorizam inventar criação, senha, kick ou transferência dinâmica. Nesta build,
os canais são configurados pelo servidor e a entrada ativa acontece pela rotina automática.

O `0x1D` é uma rota residual com contrato completo, não um request vazio. O parser S→C
`engine.dll:0x361931E0` lê `[count:u8]` e registros variáveis no fio, materializando cada entrada
em `0x2D` bytes com quatro campos `u8` e uma C-string. O callback `rakion.bin:0x00474260` guarda o
primeiro e o último byte-ID em `global+0x444A/+0x444B`. No refresh `0x174`, o cliente envia o
primeiro ID e a flag literal `1` por `SendChannelList @ 0x361911D0`. Como o World não possui esse case,
habilitá-lo na reconstrução criaria uma semântica ausente do servidor v258.

## Ping: duas famílias distintas

### `0x29/0x2A` — lista de fields

`0x29` recebe `[u16 fieldId][u32 tick]`. `FUN_00406240` encaminha ao master do field:

```text
[u16 0x29][u16 requesterGlobalSlot][u32 tick]
```

O master responde `0x2A [u16 requesterGlobalSlot][u32 tick]`; o World valida o alvo e encaminha:

```text
[u16 0x2A][u16 responderFieldIndex][u32 tick]
```

### `0x59/0x5A` — membros durante a partida

`0x59` recebe `[u8 targetSeat][u32 tick]`, valida seat `0..19` e pede ao master o ping daquele
membro. `0x5A` recebe `[u16 targetGlobalSlot][u32 tick]` e devolve o seat local do respondente.
Eles não são aliases de `0x29/0x2A`.

`0x61` também não pertence a esse par. Quando o cliente recebe S→C `0x61 [i32 value]`,
`engine.dll:0x361945B0` remonta e envia automaticamente C→S `0x61 [i32 value]`. O handler
`FUN_0041C270` grava o eco em `user+0x2380` e incrementa `world+0x51BC` quando ele coincide com
`world+0x51B4`. Portanto é um challenge/echo de valor World, não `FieldReady` nem medição de ping.

## Máquina de estados

Valores confirmados do binário:

```text
0 desconectado
1 conectado/fora do canal social
2 membro do canal e lobby de field
3 dentro da partida
4/5 grupo externo normal/especial
```

Os valores `2` e `3` são reutilizados por várias telas. O .NET ainda possui flags auxiliares
`InField` e `FieldSecondary` para compatibilidade com os guards originais, mas grupo externo,
canal social, slot local e field agora são propriedades separadas.

## Implementação e ativação

O canal padrão é criado no boot com nome `channel01`, capacidade 100, owner sentinel 100 e entrada
automática; não há flag de feature porque o fluxo é obrigatório para chegar à lista de salas. Para
adicionar canais estáticos, use `ChannelOptions`, mantendo capacidade máxima 100 e nomes/senhas
compatíveis com o cliente. Deixe `ManagedOwner=false` enquanto os requests de management estiverem
desativados.

Não ative os requests `0x1D`, `0x1F`, `0x21` ou `0x23..0x27`, nem invoque o export de canal
`SendChannelChangeBoss`, sem outra build do World e uma captura do respectivo fluxo de UI. Enviar
esse último nesta build colide com o protocolo de enchant.

Rollback seguro: conservar um único `channel01`, capacidade 100, owner sentinel 100 e entrada
automática. Não voltar a frames de presença constantes, pois eles quebram nome, classe, clã e
múltiplos jogadores.

## Validação necessária no cliente real

- abrir dois clientes com nomes longos e classes diferentes;
- confirmar entrada incremental, snapshot, chat e saída na UI;
- confirmar que o canal padrão aparece como `channel01`, sem expor o sentinel `100`;
- repetir com um usuário pertencente a clã e validar a mudança de cor do nome;
- medir os pares `0x29/0x2A` na lista e `0x59/0x5A` em PvP;
- testar disconnect abrupto e reutilização do slot local;
- conferir retorno de partida para a mesma presença de canal.

## Evidências locais

- `C:\temp\world_channel_lobby.txt`;
- `C:\temp\client_channel_lobby.txt`;
- `C:\temp\world_character_handle.txt`;
- `C:\temp\rakion_worldnet_accessor.txt`;
- `tools/ghidra/TraceWorldChannelLobby.py`;
- `tools/ghidra/DecompileClientChannelLobby.py`;
- `tools/ghidra/TraceClientClanPresence.py`;
- `tools/ghidra/FindWorldCharacterHandle.py`;
- `server/RakionServer/src/RakionServer.World/Domain/Channel.cs`;
- `server/RakionServer/src/RakionServer.World/WorldServer.ChannelLobby.cs`;
- `server/RakionServer/tests/RakionServer.World.Tests/ChannelLobbyTests.cs`.
