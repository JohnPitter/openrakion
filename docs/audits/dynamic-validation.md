# Validação dinâmica via backend — dois clientes headless

Este documento registra a validação **dinâmica** (comportamento em runtime, não só contrato
estático) executada contra o servidor .NET real, dirigida por clientes **headless** que falam o
protocolo no fio. Diferente dos testes de domínio/golden — que exercitam regras e serializam
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
  Cobre login, char-select (`0x14`), criar sala (`0x3B`), entrar (`0x38`), ready (`0x3D`),
  start (`0x43`), spawn (`0x4B`) e morte (`0x4F`).
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
| Matriz P2P local | `TwoClientP2PMatrixTests` | Direto/direto troca `0x030A` socket-a-socket sem World e rejeita duplicação TCP; direto/túnel ativa `0x54` e entrega `TunnelOne/TunnelAll` somente ao par que exige fallback, nos dois sentidos |
| Relay de combate (ataque + sync) | `TwoClientCombatRelayTests.AttackAndSyncDatagrams_RelayToOtherPeer` | Ataque `0x0311` (kind Attack) e sync `0x030F` do master chegam byte a byte ao joiner — combate no fio, não só movimento |
| Settlement PvP persistido | `TwoClientSettlementTests.PvpMatchEnd_PersistsWinLoseToDatabase` | TeamDeath com times opostos; ao encerrar (time 0 vencedor) o **motor da partida vivo** grava WIN em `characterinfo` do master e LOSE do joiner no MySQL real (delta antes/depois); W/L/D são restaurados no final |
| Matriz de modos PvP | `TwoClientModeMatrixTests` | Golem/Deathmatch/TeamDeath/Boss criam+entram+armam com 2 clientes; fragLimit fora da faixa do Deathmatch é rejeitado (disconnect `0xCC`) |
| Entrada em stage PvE solo | `SoloStageEntryTests.SoloStage_SpawnStartsStageRun` | Sala solo (stage 1) → start → spawn `0x4b` abre a execução de stage (`BeginStageRun`): `StageRunId`=`MatchId`, `ActiveStageId`=1 |
| Clear e settlement PvE | `SoloStageSettlementE2ETests.ExactReward_AppliesOnceAndIdenticalReplayOnlyAcknowledges` | Stage 1 → clear `0x4A` → rank 5 diferencial → `0x53` exato; EXP, gold, rank e ledger confirmados no MySQL. Replay idêntico recebe novo ACK sem crédito ou ledger duplicado; progressão, Cells, rank e ledgers são restaurados |
| Compra, reconnect e venda | `StoragePurchasePersistenceE2ETests.GoldPurchase_ReconnectsAndSellsWithExactPersistentDeltas` | Abre inventário `0x2C`, compra `1001` por Gold via `0x2E`, valida callbacks `0x14/0x31/0x2E`, reloga, reencontra a mesma row e saldo, vende pela célula via `0x2F` e confirma callback `0x15`, wallet e dois ledgers no MySQL; fixture restaurada |
| Chat de canal | `TwoClientChatTests.ChannelChat_BroadcastsToOtherClientInSameChannel` | Um cliente envia chat de canal (`0x22`); o outro no mesmo canal-lobby recebe o broadcast com o texto — e o remetente recebe o próprio eco |
| Ciclo vivo de partida | `TwoClientLiveMatchLifecycleTests.DeadlineAndDeathFrame_AdvanceRoundAndEndMatch` | Golem com times opostos: primeiro spawn `0x4B`, `Pre→Playing` pelo deadline no motor global, entrada tardia do segundo jogador, morte própria `0x4F` no fio, broadcasts `0x4F/0x4A`, vitória do round e fim do match `0x44` pelo próximo tick |
| Bot no fio | `BotMovementE2ETests` e `BotStageValidationTests` | Movimento sintético recebido; perseguição converge; dois humanos recebem o bot; o envelope DLL `0xB07A` entrega ataque sem relay duplicado; primeiro ataque reduz HP e devolve `0x0311 kind=Damage`; golpes seguintes matam o bot e publicam `0x4F`. Smoke gráfico permanece separado |

Todos verdes: **816 testes World**, dos quais **23 são E2E** no fio. Descobertas de RE confirmadas em runtime:

- o transporte do cliente e do servidor é simétrico na cifra (AES-128 do canal lobby) mas
  **assimétrico no envelope**: cliente→servidor carrega `[opcode][seq]`, servidor→cliente carrega
  o frame já pronto pelo primeiro byte (`0x0C`/`0x0D`/`0x10`) ou `[msgType][data]`;
- `FieldLobby == LoggedIn == 2`: o gate real de criar/entrar em sala não é o `Status`, e sim o
  **personagem selecionado** (`ActiveCharId>0`, contrato `WorldRequestGatePolicy`), senão o
  `0x3b` cai em disconnect `0x52` — comportamento reproduzido e documentado pelo harness;
- o endpoint UDP relayado é o **observado** (`from` real do socket), não o anunciado — o relay de
  gameplay volta ao socket de origem, então o handshake basta para o peer receber o tráfego;
- no par direto/direto, `0x030A` trafega diretamente entre os sockets headless e `0x56/0x57`
  ficam silenciosos. Quando um peer não anuncia endpoint, o `0x45` ativa o agregado `0x54`, e
  `TunnelOne/TunnelAll` usam `0x57` apenas no par direto/túnel, sem eco ao sender;
- `CreateField` publica a sala em duas etapas: `JoinField` grava `session.FieldId` **antes** de
  `Fields.Add(field)`. Há uma janela em que `FieldId>=0` mas `GetField` ainda é nulo — quem
  observa o estado da sala deve esperar `GetField != null`, não só `FieldId>=0` (o harness faz
  isso);
- o enum de domínio é `Golem=1, Deathmatch=2, TeamDeath=3, Boss=4` — **não** confundir com rótulos
  intuitivos; o fragLimit valida por modo do wire (Deathmatch 13..30, TeamDeath 20..50);
- o primeiro spawn `0x4B` marca apenas quem carregou como `state=4`; vencido o deadline, o motor
  global inicia o round se houver ao menos um jogador carregado e mantém os demais em `state=3`.
  Um spawn tardio promove o segundo jogador, e sua morte `0x4F` atravessa dispatch, placar,
  `RoundEnd`, `0x4A` e encerramento `0x44` sem chamar a regra de domínio pelo teste;
- o `0x53` de stage PvE é **anti-cheat**: `CanSettleStage` exige `Phase==RoundEnd`, stage cleared,
  reward diferencial exato e Cell EXP coerente com equipamento/Power User. O E2E fecha
  `0x4A→0x53→ACK`, persistência e replay idempotente pelo mesmo socket e banco real;
- a compra Gold percorre a UI de inventário e o dispatch reais (`0x2C→0x2E`), só publica os
  callbacks `0x14/0x31/0x2E` depois do commit e reaparece no reconnect com a mesma row/serial. A
  venda `0x2F→0x15` remove essa instância, credita a fórmula `40%` e fecha o segundo ledger;

## Como rodar

```powershell
# com o stack de dev de pé (container rakion-db em 3306, base rakion seed test/test2)
dotnet test server/RakionServer/tests/RakionServer.World.Tests -c Release `
  --filter "FullyQualifiedName~E2E"
```

Sem banco, os testes fazem skip suave (não quebram o CI). Para apontar outro banco, exporte
`RAKION_E2E_CONNECTION`.

## Fronteira dinâmica restante

Coberto no backend: login → char-select → sala → join → ready → start → engage pelo tick real →
spawn tardio → morte `0x4F` → placar/fim de round/fim de match → handshake UDP → relay de
movimento **e combate** (`0x030A`/`0x0311`/`0x030F`) → settlement PvP persistido no DB → matriz
dos 4 modos → entrada, clear e settlement de stage PvE solo → compra/venda Gold com reconnect →
matriz P2P local direto/TunnelOne/TunnelAll → bot server-side no fio. Ainda **não** exercitado
headless (próximos alvos):

- **economia/UI ao vivo**: variantes Cash/cupom/bundle, enchant, presentes, Power User e ranking
  pelos frames reais.

E a camada que **exige cliente gráfico** (fora do escopo backend): animação, frames de ataque,
hitbox, colisão, trajetória de projétil, efeitos, render de HUD/ranking e a topologia P2P do engine
real em LAN, NAT diferente e UDP bloqueado.
