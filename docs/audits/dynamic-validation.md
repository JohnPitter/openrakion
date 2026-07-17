# Validação dinâmica via backend — dois clientes headless

Este documento registra a validação **dinâmica** (comportamento em runtime, não só contrato
estático) executada contra o servidor .NET real, dirigida por clientes **headless** que falam o
protocolo no fio. Diferente dos 768 testes de domínio/golden — que exercitam regras e serializam
frames em memória — os testes E2E aqui sobem um `WorldServer` vivo (TCP + AES + dispatch + motor
de partida + banco) e conectam dois clientes por sockets reais.

Isso ataca a maior pendência registrada em [`re-status-summary.md`](re-status-summary.md): a
fronteira dinâmica. Não substitui a validação **gráfica** com o cliente v258 (animação, hitbox,
render), mas prova que o servidor porta a jornada multi-cliente ponta a ponta no backend.

## Harness

`server/RakionServer/tests/RakionServer.World.Tests/E2E/`:

- **`HeadlessWorldClient`** — cliente sem interface. Fala o transporte real: frame
  `[u16 size][AES-128(content)]` com a chave/IV do canal lobby/field
  (`PacketCrypto.EnableWorldDefault`), plaintext cliente→servidor `[u16 opcode][u16 seq][data]` e
  a checagem de sequência. Decodifica os frames servidor→cliente e expõe uma fila para asserts.
  Cobre login, char-select (0x14), criar sala (0x3b), entrar (0x38), ready (0x3d) e start (0x43).
- **`WorldServerFixture`** — sobe um `WorldServer` real em loopback (portas próprias
  41708/41709/41706, sem colidir com o stack de produção). Gate suave: sem banco acessível a suíte
  faz *skip* (igual aos `*DatabaseSmokeTests`). Conexão via `RAKION_E2E_CONNECTION` ou o padrão do
  stack de dev (`root/123456 @ localhost:3306`, base `rakion`, seed `test`/`test2`).
- **`E2ECollection`** — serializa os testes (portas fixas compartilhadas, sem paralelismo).

Seed usado (banco `rakion`): conta `test` → personagem `GoHeroi` (`#1`); conta `test2` →
`ProbeTwo` (`#9001`).

## O que foi validado no fio

| Cenário | Teste | Prova dinâmica |
|---|---|---|
| Login concorrente de 2 clientes | `TwoClientLoginTests.TwoHeadlessClients_LoginConcurrently_...` | Ambos autenticam via AES real; servidor emite `0x0C`+`0x0D`+`0x10`; duas sessões distintas, `CurrentUsers==2`, contas corretas |
| Credencial inválida | `TwoClientLoginTests.HeadlessClient_LoginWithWrongPassword_IsRejected` | Senha errada não gera char-list nem promove sessão |
| Criar + entrar em sala Golem | `TwoClientRoomTests.TwoHeadlessClients_CreateAndJoinGolemRoom_...` | Master cria field competitivo (mode Golem), joiner entra por `0x38`; ambos no mesmo `Field`, assentos distintos, papel de master preservado |
| Ready + start da partida | `TwoClientMatchStartTests.TwoHeadlessClients_ReadyAndStart_...` | Joiner marca ready (`0x3d`); master inicia (`0x43`); partida armada (fase Pre, `MatchId`, deadline de engajamento no futuro); ambos os assentos promovidos a combatente (`State==3`) |
| Gameplay UDP + relay de movimento | `TwoClientGameplayUdpTests.TwoHeadlessClients_UdpHandshakeAndMoveRelay_...` | Ambos autenticam o endpoint UDP (handshake `0x0202`, validação de slot+IP+chave); echo `0x0201` retorna; um movimento `0x030A` do master é relayado **byte a byte** ao outro peer do mesmo field, com o assento de origem preservado |
| Relay de combate (ataque + sync) | `TwoClientCombatRelayTests.AttackAndSyncDatagrams_RelayToOtherPeer` | Ataque `0x0311` (kind Attack) e sync `0x030F` do master chegam byte a byte ao joiner — combate no fio, não só movimento |
| Settlement PvP persistido | `TwoClientSettlementTests.PvpMatchEnd_PersistsWinLoseToDatabase` | TeamDeath com times opostos; ao encerrar (time 0 vencedor) o **motor da partida vivo** grava WIN em `characterinfo` do master e LOSE do joiner no MySQL real (delta antes/depois) |
| Matriz de modos PvP | `TwoClientModeMatrixTests` | Golem/Deathmatch/TeamDeath/Boss criam+entram+armam com 2 clientes; fragLimit fora da faixa do Deathmatch é rejeitado (disconnect `0xCC`) |
| Entrada em stage PvE solo | `SoloStageEntryTests.SoloStage_SpawnStartsStageRun` | Sala solo (stage 1) → start → spawn `0x4b` abre a execução de stage (`BeginStageRun`): `StageRunId`=`MatchId`, `ActiveStageId`=1 |
| Chat de canal | `TwoClientChatTests.ChannelChat_BroadcastsToOtherClientInSameChannel` | Um cliente envia chat de canal (`0x22`); o outro no mesmo canal-lobby recebe o broadcast com o texto — e o remetente recebe o próprio eco |

Todos verdes junto dos 768 de domínio (**782 total**, 14 testes E2E). Descobertas de RE confirmadas em runtime:

- o transporte do cliente e do servidor é simétrico na cifra (AES-128 do canal lobby) mas
  **assimétrico no envelope**: cliente→servidor carrega `[opcode][seq]`, servidor→cliente carrega
  o frame já pronto pelo primeiro byte (`0x0C`/`0x0D`/`0x10`) ou `[msgType][data]`;
- `FieldLobby == LoggedIn == 2`: o gate real de criar/entrar em sala não é o `Status`, e sim o
  **personagem selecionado** (`ActiveCharId>0`, contrato `WorldRequestGatePolicy`), senão o
  `0x3b` cai em disconnect `0x52` — comportamento reproduzido e documentado pelo harness;
- o endpoint UDP relayado é o **observado** (`from` real do socket), não o anunciado — o relay de
  gameplay volta ao socket de origem, então o handshake basta para o peer receber o tráfego;
- `CreateField` publica a sala em duas etapas: `JoinField` grava `session.FieldId` **antes** de
  `Fields.Add(field)`. Há uma janela em que `FieldId>=0` mas `GetField` ainda é nulo — quem
  observa o estado da sala deve esperar `GetField != null`, não só `FieldId>=0` (o harness faz
  isso);
- o enum de domínio é `Golem=1, Deathmatch=2, TeamDeath=3, Boss=4` — **não** confundir com rótulos
  intuitivos; o fragLimit valida por modo do wire (Deathmatch 13..30, TeamDeath 20..50);
- o `0x53` de stage PvE é **anti-cheat**: `CanSettleStage` exige `Phase==RoundEnd`, stage cleared e
  exp/gold **exatamente** iguais ao reward calculado (`StageRewardPolicy`) — por isso a validação
  headless cobre a ENTRADA da run (`BeginStageRun`); a liquidação com reward exato fica nos testes
  de domínio/DB.

## Como rodar

```powershell
# com o stack de dev de pé (container rakion-db em 3306, base rakion seed test/test2)
dotnet test server/RakionServer/tests/RakionServer.World.Tests -c Release `
  --filter "FullyQualifiedName~E2E"
```

Sem banco, os testes fazem skip suave (não quebram o CI). Para apontar outro banco, exporte
`RAKION_E2E_CONNECTION`.

## Fronteira dinâmica restante

Coberto no backend: login → char-select → sala → join → ready → start (armada) → handshake UDP →
relay de movimento **e combate** (`0x030A`/`0x0311`/`0x030F`) → settlement PvP persistido no DB →
matriz dos 4 modos → entrada em stage PvE solo. Ainda **não** exercitado headless (próximos alvos):

- **ciclo de partida ao vivo pelo tick real**: engage por deadline (Pre→Playing), rounds,
  morte/respawn dirigidos por `0x4f` no fio e placar (hoje o settlement é validado forçando o
  fim-de-partida; a cadeia de morte→round é coberta por testes de domínio);
- **liquidação 0x53 com reward exato** (anti-cheat): coberta por `StageSettlementDatabaseSmokeTests`;
- **matriz P2P** (direto/TunnelOne/TunnelAll, mesma máquina/LAN/NAT/UDP bloqueado);
- **economia/UI ao vivo**: loja, inventário, enchant, presentes, Power User e ranking pelos frames
  reais.

E a camada que **exige cliente gráfico** (fora do escopo backend): animação, frames de ataque,
hitbox, colisão, trajetória de projétil, efeitos e render de HUD/ranking.
