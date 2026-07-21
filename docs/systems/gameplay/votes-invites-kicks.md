# Engenharia reversa de votos, convites e expulsões — Rakion v258

## Escopo e veredito

Este documento cobre convite para field, habilitação de convite, votação de kick, voto,
elegibilidade, timeout, expulsão direta do host e mudança forçada de time.

**Veredito:** `0x40`, `0x5D/0x5E/0x5F`, `0x5B` e `0x72` estão reconstruídos e implementados. A votação foi
validada no dispatcher real com três sessões: abertura somente ao terceiro eleitor, rejeição do
voto do alvo e resultado final idêntico ao serializer original. O voto aprovado não remove o alvo
diretamente no World: publica o resultado e grava uma penalidade de reentrada de 30 minutos; o
cliente decide sua saída a partir do `0x5F`. O `0x40` foi validado com três sessões em partida,
incluindo cancelamento de voto, saída, fim de round e retorno da vítima ao lobby. A apresentação
gráfica dos diálogos e da transição ainda permanece sem validação.

## Contratos exportados pelo cliente

| Opcode | Export | Payload conhecido |
|---:|---|---|
| `0x40` | `SendFieldKick` | `[u8 slot]` |
| `0x5B` | `SendFieldForceChangeTeam` | `[u8 slot]` |
| `0x5D` | `SendFieldGameVoteOpen` | `[u8 target][cstr reason]` |
| `0x5E` | `SendFieldGameVote` | `[u8 vote]` |
| `0x72` | `SendFieldInvitation` | `[u16 targetSessionSlot]` |

Esses nomes indicam intenção do cliente, mas não bastam para escolher a rota da build: a jump table
do `worldserv.exe` reconstruído contradiz quase todos eles.

## Conflitos da build analisada

### `0x5B`

`SendFieldForceChangeTeam @ engine.dll:0x36192970` envia `[u16 0x5B][u8 targetSeat]`.
`FUN_00425990` exige handles, `Status=3`, `SubStatus=1`, field fora do estado `2`, remetente no seat
master e target `< 0x12`. `FUN_00409080` só aceita target cujo estado `+0x126` seja `1` ou `2` e
delega a `FUN_004075A0`.

O helper procura o primeiro seat vazio no bloco oposto (`0..9` ou `10..19`), copia o record inteiro,
zera a origem e atualiza master/estado associado. Sucesso publica a todos
`0x3E [status=0][oldSeat][newSeat]`; target pronto/bloqueado ou time cheio recebe somente
`0x3E [status=2]`. Estado de target diferente de `1/2`, field já em match ou target `>= 18` são
ignorados sem resposta. O port anterior inventava `0x5B [sender][action]` e não mutava o domínio;
isso foi substituído por `Op_FieldForceChangeTeam` e testes de movimento/negação.

### `0x5D`, `0x5E` e `0x5F`

Os builders do cliente são exatos:

- `SendFieldGameVoteOpen @ engine.dll:0x361929D0` envia
  `[u16 0x5D][u8 targetSeat][cstr reason]`;
- `SendFieldGameVote @ engine.dll:0x36192A40` envia `[u16 0x5E][u8 vote]`.

O dispatcher S→C `engine.dll:0x36197320` roteia `0x5D` para `FUN_36194360`, que lê
`[targetSeat][reason cstr]` e chama o callback `+0x28C`. `0x5F` vai a `FUN_361943D0`: o primeiro
byte é status; somente quando ele é zero o cliente lê os seis bytes seguintes
`[result][eligible][yes][no][abstain][target]`, enviados ao callback `+0x290`.

`FUN_00425A70/00425BB0` resolvem o seat do remetente e chamam o agregado
`FUN_0040A420`. Só o master pode abrir, há no mínimo três records em estado `4`, e o prazo é
`GetTickCount()+60000`. O opener recebe voto `1` automaticamente. A abertura `0x5D` vai apenas aos
players `state=4`, excluindo opener e alvo.

Cada player possui um byte de voto em `record+0x134`: `0` pendente, `1` sim, `2` não e `3`
abstenção. O alvo não vota. A apuração considera apenas `state=4` e encerra quando todos os
eleitores exceto o alvo votaram ou quando o prazo termina. O resultado FIELD tem nove bytes:

```text
[u16 0x5F][u8 status=0][u8 result=0]
[u8 eligible][u8 yes][u8 no][u8 abstain][u8 targetSeat]
```

A proposta passa quando houve participação `sim+não` de pelo menos metade dos elegíveis e
`yes>no`. Nesse caso, `FUN_004068C0` reserva um dos dez slots `field+0x358/+0x35C` com a identidade
do alvo e expiração de 1.800.000 ms. `FUN_00406F40`, no join, devolve status `8` enquanto a
penalidade está vigente. O World original não chama sua rotina de remoção nesse ponto.

Erros são respostas lobby curtas `[u16 0x5F][u8 status]`: `1` voto já ativo, `2` já votou, `4`
inativo, `5` alvo tentando votar, `6` tabela de penalidades cheia, `7` não-master e `9` menos de
três players. O código `3` existe no helper para target divergente, mas o agregado passa o próprio
target armazenado. `8` é retorno interno de apuração pendente; não vira ACK porque
`FUN_004098E0` zera `AL` depois da chamada.

O port anterior tratava os dois opcodes como chat e respondia sucesso sem estado. Foi substituído
por `FieldVote`, tick no motor do field, serializers `0x5D/0x5F`, penalidade de reentrada e prova
`world_vote_probe.py`. A prova produziu:

```text
open, somente terceiro eleitor: 5d000141464b00
alvo tentando votar:            5f0005
resultado aos três:             5f0000000302000001
```

### `0x72`

O nome C++ do parâmetro exportado não revela a semântica; o handler original `FUN_00428520` prova
que o word é o **slot global da sessão alvo**. O remetente precisa estar com os dois handles de
field; o alvo precisa ter seu primeiro handle de field ativo. O original usa `Status=3`. Como o
servidor .NET representa separadamente a espera na sala (`FieldLobby=2`) e a partida
(`InField=3`), o handler aceita os dois estados. Falhas usam `DISC D6`, `D7` e `D8`, nessa ordem.

A resposta vai apenas ao alvo pelo canal lobby:

```text
[u16 0x72]
[u16 inviterSessionSlot]
[cstr inviterName]
[u16 fieldRef]
[u8 map][u8 mode][u8 rule111][u8 rule112][u8 rule113]
[u8 maxRounds][u16 roundDuration]
[cstr roomName][cstr roomDescription]
```

O blob final é exatamente `FUN_00406A80`. `rule111/112/113` preservam os offsets originais; na
criação da sala correspondem aos três bytes recebidos depois de frag limit, ainda sem nomes de UI
confirmados para todos os modos. O servidor agora usa o field real do remetente, serializa todos os
campos e deixa `0x72` chegar ao dispatcher durante `Status=3`.

No probe de duas sessões, o alvo recebeu
`72 00 00 00 "JP" 00 00 00 01 03 01 63 00 01 B0 01 "RECombat" 00 "battle" 00`.
O convite não exige `PendingInvitation`, accept/decline ou timeout server-side para paridade com
esta build; políticas adicionais seriam uma extensão do produto, não parte comprovada do v258.

### `0x40`

`FUN_00423CC0` exige os dois handles, `Status=3` e resolve o seat do remetente. Categoria normal
(`user+0x146C=0`) precisa ser o master em `field+0x121`; categorias `Special=1` e `GM='4'` passam
desse gate. O byte C→S é o seat alvo. `FUN_004097C0` ignora record vazio, bloqueado (`state=5`) ou
alvo de categoria `Special=1`, e delega a saída a `FUN_004091E0`.

A saída cancela uma votação ativa quando a vítima é o alvo, publicando
`0x5F [status=0][result=1][0][0][0][0][target]` sem penalidade; limpa o record; atualiza contagens;
publica `0x3A [seat]`; reavalia eliminação/quantidade/leader conforme o modo e publica `0x4A` quando
o round termina. Se era master, escolhe primeiro outro record `state=4`, depois `state=3`, depois
qualquer ocupado, e publica `0x3C [newMaster]`. `FUN_0041B8B0 → FUN_0040AF60` devolve a vítima ao
canal com `Status=2`; não fecha sua conexão.

O port centraliza essa sequência em `WorldServer.RemoveFieldMember`. A prova
`world_field_kick_probe.py` abriu um voto contra seat 1 e produziu:

```text
host e eleitor:  5f0000010000000001
                 3a0001
                 4a0001010100
vítima:          1f... 1e... 3600...
nova requisição: 3600... (mesma conexão ativa)
```

## Funcionalidades ausentes

### Convites

O transporte original está completo. Ainda faltam validação gráfica do popup e hardening contra
flood. Preferência invite on/off, bloqueio social, expiração e accept/decline server-side não foram
observados neste contrato e só devem ser adicionados como funcionalidade nova, com opcode/UI
compatíveis, sem alterar o frame `0x72` legado.

### Votação

O contrato legado, estado, timeout, apuração e penalidade estão implementados. Permanecem:

- validação do popup e da saída automática no cliente gráfico;
- captura visual das mensagens correspondentes aos status `1..9`;
- rate limit adicional contra spam, que seria hardening do produto e não regra observada;
- política explícita para reconexão de conta em outra instância do World.

### Mudança forçada de time

A operação de domínio ligada a `0x5B` agora preserva o record e segue os gates do original. O
World original já proíbe a operação quando `field+8 == 2`; portanto ela não é um respawn nem uma
troca durante round ativo. Falta reproduzir graficamente a janela exata em que a UI habilita o
comando, pois o fluxo headless atual passa de `Status=2` para `field.State=2` antes de expor a mesma
fase intermediária do servidor original.

## Regras recomendadas

### Convite — extensão opcional

```text
Invitation
  Id, RoomId, InviterId, InviteeId
  CreatedAt, ExpiresAt, Status
```

- preservar o `0x72` legado como transporte e não exigir estado pendente para clientes v258;
- inviter deve ser membro da sala e ter permissão definida;
- alvo deve estar online, disponível e aceitar convites;
- aceitar executa o mesmo `Room.Join` usado pela entrada normal;
- nunca contornar senha/capacidade/level/ban por meio de convite;
- no máximo um convite equivalente pendente e limites por origem/alvo/IP.

### Vote kick — extensão opcional além do legado

```text
VoteKick
  Id, MatchId, InitiatorSeat, TargetSeat, ReasonCode
  EligibleVoters, Votes, OpenedAt, ExpiresAt, Status
```

- preservar o agregado legado como golden source e colocar regras novas atrás de configuração;
- target e initiator precisam pertencer ao mesmo match;
- target não vota; contas/sessões duplicadas contam uma vez;
- elegibilidade é congelada na abertura, com política explícita para disconnect;
- quorum e maioria devem ser configurados por número de elegíveis;
- execução publica um único `PlayerKicked` e fecha a votação;
- reason textual, se mantido, passa por limite, moderação e log seguro.

Host kick na sala e voto no match são comandos diferentes. A build v258 não usa uma rotina comum
de remoção: o voto aprovado publica `0x5F` e instala a penalidade temporária.

## Remoção atômica

Uma expulsão deve, sob lock do room/match:

1. validar host ou resultado da votação;
2. marcar membro como removendo e invalidar ações novas;
3. remover peer/seat/player state;
4. decidir resultado/penalidade conforme estado e motivo;
5. transferir host se necessário;
6. publicar evento aos membros e resposta à vítima;
7. levar a sessão ao estado correto de lobby;
8. ser idempotente para kick, leave e disconnect concorrentes.

## Segurança e abuso

- o alvo é um slot global; validar presença reduz spoof e slot stale;
- `cstr reason` precisa de comprimento e sanitização antes de log/UI;
- flood de convites/votações pode assediar ou travar a UI;
- múltiplas sessões da mesma conta podem manipular quorum;
- host não pode expulsar por opcode conflitante sem autorização da rota;
- expulsão não pode conceder vitória, evitar derrota ou duplicar settle;
- mudanças forçadas de time não podem favorecer score já em andamento.

## Implementação e ativação

O fluxo legado já está ativo sem feature flag. Para reproduzir a prova:

1. iniciar MariaDB e o World com `deploy/worldserver.ini`;
2. manter as fixtures `test`, `test2` e `test3`, personagens `1`, `2` e `3`;
3. executar `python tools/world_vote_probe.py 40708`;
4. executar `python tools/world_field_kick_probe.py 40708`;
5. validar no cliente gráfico a janela de abertura, os botões e as duas jornadas de saída.

```ini
[SocialMatch]
InvitationHardening=false
HostKick=false
VoteKickHardening=false
ForceTeam=false
```

Esse bloco é uma configuração **proposta**, ainda não existente no servidor. Os transportes
legados `0x5D/0x5E/0x5F` e `0x72` já estão ativos sem feature flag. Ativar extensões em canal de teste;
rollback impede novas operações e cancela votos/convites estendidos pendentes, sem reintegrar
automaticamente jogadores já removidos.

## Testes mínimos

- golden do frame `0x72`, alvo inexistente e alvo sem handle de field;
- popup e ação resultante validados com dois clientes gráficos;
- para a extensão opcional: recusado, expirado, duplicado, sala cheia/em jogo e preferência off;
- abrir voto sem permissão, tabela cheia e menos de três players;
- eleitor duplicado, alvo tentando votar, timeout e join sob penalidade;
- maioria/quorum nos tamanhos 3, 4 e 8;
- timeout simultâneo ao último voto;
- host kick, vote kick, leave e disconnect concorrentes;
- vítima retorna ao lobby e peers deixam de vê-la;
- force-team antes/depois de ready e durante round;
- golden frames e validação visual com pelo menos dois clientes.

## Critério de conclusão

Esta frente ficará completa quando os popups `0x72/0x5D/0x5F`, o kick `0x40` e a saída automática
após voto forem validados graficamente. Os contratos wire e o domínio estão fechados; a penalidade
de 30 minutos segue o World original e não deve ser substituída por remoção server-side sem
evidência do cliente.
