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

Todos verdes junto dos 768 de domínio (**772 total**). Descobertas de RE confirmadas em runtime:

- o transporte do cliente e do servidor é simétrico na cifra (AES-128 do canal lobby) mas
  **assimétrico no envelope**: cliente→servidor carrega `[opcode][seq]`, servidor→cliente carrega
  o frame já pronto pelo primeiro byte (`0x0C`/`0x0D`/`0x10`) ou `[msgType][data]`;
- `FieldLobby == LoggedIn == 2`: o gate real de criar/entrar em sala não é o `Status`, e sim o
  **personagem selecionado** (`ActiveCharId>0`, contrato `WorldRequestGatePolicy`), senão o
  `0x3b` cai em disconnect `0x52` — comportamento reproduzido e documentado pelo harness.

## Como rodar

```powershell
# com o stack de dev de pé (container rakion-db em 3306, base rakion seed test/test2)
dotnet test server/RakionServer/tests/RakionServer.World.Tests -c Release `
  --filter "FullyQualifiedName~E2E"
```

Sem banco, os testes fazem skip suave (não quebram o CI). Para apontar outro banco, exporte
`RAKION_E2E_CONNECTION`.

## Fronteira dinâmica restante

Coberto no backend até aqui: login → char-select → sala → join → ready → start (partida armada).
Ainda **não** exercitado headless (próximos alvos):

- **gameplay UDP**: handshake das portas, movimento `0x030A`, combate/dano, tick 1583 e relay
  entre peers no field armado;
- **ciclo de partida completo**: engage (Pre→Playing), rounds, morte/respawn, placar e o
  settlement persistido (`SettleMatchAsync`) com dois clientes;
- **PvE stage**: `0x4b` spawn, clear/derrota, `0x53` result e a liquidação de exp/gold/rank;
- **matriz de modos** (Golem/TeamDeath/Deathmatch/Boss) e a matriz P2P
  (direto/TunnelOne/TunnelAll, mesma máquina/LAN/NAT/UDP bloqueado);
- **economia/UI ao vivo**: loja, inventário, enchant, presentes, Power User e ranking dirigidos
  pelos frames reais.

E a camada que **exige cliente gráfico** (fora do escopo backend): animação, frames de ataque,
hitbox, colisão, trajetória de projétil, efeitos e render de HUD/ranking.
