# Auditoria de qualidade de código — OpenRakion

> Auditoria iniciada em 2026-07-16 e reconciliada com o código atual em 2026-07-18.
> Contexto respeitado: comentários `FUN_xxxx`/offsets/MITM são **documentação de RE** (manter);
> AES-128-ECB é a cifra **intencional** do jogo; credenciais de amostra (`root/123456`, `test/test`)
> são abertas de propósito (servidor pessoal offline). Esses itens **não** são apontados como defeito.
>
> Gates derivados desta auditoria estão em [`CLAUDE.md`](../../CLAUDE.md#code-quality-gates).

## Panorama — arquivos acima do alvo (~400 linhas)

| Arquivo | Linhas | Excesso | Natureza |
|---|---:|---:|---|
| `World/WorldServer.cs` | 1304 | 3.3x | host de infra **+ casos de uso + motor de partida** misturados |
| `World/Database/WorldDatabase.cs` | 976 | 2.4x | núcleo do repositório ainda grande, apesar dos partials por domínio |
| `World/Domain/Field.cs` | 614 | 1.5x | domínio **+ wire-codec** misturados |
| `World/Network/ClientSession.cs` | 445 | 1.1x | socket, dispatch e estado de sessão ainda acoplados |
| `World/Network/WorldHandlers.ReconCombatB.cs` | 420 | 1.1x | seat + relay de combate |

## Plano priorizado

### P0 — Remover código morto

- ✅ A tabela World foi achatada: `WorldHandlers.Generated.cs`, `RegisterGenerated`, `Stub`,
  `StubGates`, `ReconStub`, `RoomMgmt.cs`, `RoomSettings.cs` e os aliases incompatíveis já foram
  removidos. `WorldHandlers.cs` aponta diretamente para os delegates finais.
- ✅ **Aliases privados sem chamador**: removidos após auditoria cruzada da tabela canônica e dos
  interceptores por estado; nenhum método `Op_*` privado permanece com apenas a própria declaração.
- ✅ `SendInventoryList`, `DiagEmptyInventory`, `LoadClanAsync` e `InsertUserItemAsync` foram
  removidos. `LoadCharactersAsync` é a consulta canônica usada na montagem do login `0x0C`.
- ✅ `ServerListener`, `CheckServerExpired`, a branch `extip` inerte e `web/launcher_web.py` foram
  removidos. O Broker usa `Common.IniFile` cross-platform.
- ✅ O reader duplicado do Broker foi eliminado; IPC/login usa `Common.PacketReader`. O writer da
  lista de mundos expõe somente byte, `ushort` e finalização do frame, protegidos por goldens.
- `tools/difftest.py` permanece intencional: dirige um World nativo ou .NET já em execução; o
  `OracleDiff` compara oráculos/capturas por outro fluxo. Ambos estão documentados em `tools/README.md`.

### P1 — Quebrar os god-files (estrutura)
- ✅ O antigo `WorldHandlers.Generated.cs` foi dividido em partials por domínio e eliminado; a
  tabela canônica fica em `WorldHandlers.cs`.
- ✅ **Broker split e cross-platform**: `Systems.cs` caiu para 260 linhas; client, decode, server,
  server-info e codec estão separados. `Ini/Profile` via P/Invoke foi substituído por
  `Common.IniFile`, e o parser do Broker usa o reader comum seguro.
- **`ClientSession.cs`** → ✅ **replay eliminado** (2026-06-17): os `_rNN` da cadeia lobby→canal→sala→stage viraram síntese pura em `LobbyFrames` (golden-testada byte-a-byte); `OracleReplay.cs`→`LobbyFlow.cs` (só fluxo/dispatch); o `oracle_*.bin` do login já fora p/ `LoginCharListWriter`. Resta extrair `InventoryProtocol`/`ShopSession` e agrupar o god-object de estado em `CharProgress`/`FieldState`/`Wallet`.

### P2 — Separar domínio de infra (regra de negócio fora de I/O/rede)
- **Economia**: compra/venda de storage, Power User e Stage/PvE usam casos de uso no
  `WorldServer` e transações idempotentes no `WorldDatabase`. Stage é calculado pelo backend a
  partir do catálogo, rank e melhor rank anterior. PvP preserva o reporte legado limitado pelo
  `GamePointRules`; torná-lo server-calculated seria uma extensão de autoridade.
- **Motor de partida** (`MatchTick`/`SettleMatch`/`GrantExp`/clocks) em `WorldServer` → extrair `MatchEngine`.
- **Wire-codec em entidade de domínio**: `Field.Build0x48/49/4a`/`BuildMatchEnd`/`SerializeListEntry` → mover para um `FieldWireCodec`/camada Network; `Combat_*` de `ReconCombatA` viram métodos de `Field`/`PlayerRec`.

### P3 — Dedup + segurança por construção
- **Gate de field duplicado em ~40 handlers** (`if (!(u.InField && u.FieldSecondary)) {Disconnect} if (Status != …) {Disconnect}`) — o helper `ReconGate` já existe em `Recon.cs:86` mas não é usado uniformemente; criar `ReconGate2(ctx, discNoField, discBadStatus)` e aplicar. Padronizar `UserStatus.InField` em vez de `0x03` cru.
- ✅ **`PacketReader` seguro por construção**: todas as primitivas, `Skip` e o offset inicial
  validam os limites e convergem em `EndOfPacketException`; a comparação evita overflow de soma.
- **`MapCharacter`/`StatColumns`/`WithConnection`** em `WorldDatabase`; `LogConsole`↔`LogDebug`
  no Broker (~70 ln copy-paste). Os settlements `0x50/0x53` já foram separados por domínio e
  compartilham apenas a projeção canônica de progressão.
- `0x2D` foi consolidado em `Op_InventoryLeave`; o interceptor e o nome incorreto
  `Op_RoomRosterSync` foram removidos.

## Bugs latentes (não são só estilo — corrigir)

- ✅ **`PacketReader` com bounds-check integral** (`Common/PacketReader.cs`): primitivas, strings,
  bytes, `Skip` e offset inicial rejeitam frames curtos/forjados com `EndOfPacketException`; o gate
  usa `n <= Remaining`, sem overflow na soma.
- ✅ **Opcode `0x4A` consolidado**: a decompilação confirmou `[RoundEndReason,LosingSideWire,Wins0,Wins1]`; todos os emissores usam `Field.Build0x4a()` e há golden tests do wire.
- ✅ **Capacidade de sala sem truncamento**: `RoomCreationOptions.Capacity` e `Field.MaxPlayers`
  são `byte`, limitados a 20 pelo contrato v258; não existe mais conversão de `ushort`.
- ✅ O alias morto `Op_FieldCreateRoomEntry` foi removido; a criação canônica valida o domínio em
  `ClientSession.Rooms`.
- **Power User**: compra, alocação e callback foram fechados com transações autoritativas; permanece validação gráfica. O protocolo legado não carrega uma chave de retry, portanto idempotência após reconexão exigiria extensão coordenada de cliente e servidor. **[BAIXA]**
- ✅ **`EchoGameplayUdp` com frame completo**: o roteamento exige
  `GameplayUdpHandshake.PacketSize` (23 bytes), e o parser repete o mesmo limite antes de ler
  `EchoData` em `packet[19..23]`.
- **Resolvido — Buddy CDs**: `BuddyProtocol.cs` é a fonte única; `RET_SET_NICK=0x3101` e `RET_GROUP_GETLIST=0x3151`, sem literais divergentes no dispatcher.

## Segurança (real — fora os itens intencionais)

- ✅ **Parse externo com bounds-check**: `PacketReader` é seguro por construção; o Broker rejeita
  UDP abaixo do frame IPC mínimo ou sem CRC antes de criar o reader, e gameplay UDP usa parser
  por `ReadOnlySpan` com tamanho fixo.
- **Senhas de conta em texto puro** no DB (`LauncherWeb/Program.cs:38`, `AdminDb.CreateAccount/SetPassword`). **Imposto pela fidelidade ao cliente** (o launcher manda `pass` como hex de latin1, comparação direta) — não corrigível sem mexer no cliente. **Registrar como risco conhecido**: um dump do DB expõe todas as senhas. **[MÉDIA, aceito]**
- **Crédito PvP de gold/exp a partir de input do cliente**: `GamePointRules` replica
  `FUN_0041CF80` (`115/160` Deathmatch/Boss, `90/70` Team Death e limites de Golem por rodada),
  mas valores dentro do teto continuam reportados pelo cliente como no original. Stage não usa
  esse caminho: recompensa e Cell EXP são calculados/validados pelo backend. **[MÉDIA, legado]**
- ✅ **Credencial redigida no log**: `ClientSession.ReceiveLoopAsync` registra somente o tamanho do
  receive em `Debug`; o payload decifrado do opcode de login `0x0C` é substituído por
  `<nB redacted>`. Outros opcodes permanecem disponíveis em hex apenas com debug habilitado.

## O que está bem (referência de qualidade)

Código novo, idiomático e limpo — usar como padrão ao refatorar o resto:
- **`RakionServer.Buddy`** — escrito do zero, DTO de frame explícito, logs nos fluxos certos.
- **`RakionServer.Admin`** (incl. `AdminDb`) — **SQL 100% parametrizado, sem injection**; records/DTOs; vertical slice.
- **`RakionServer.LauncherWeb`** — minimal-API enxuta, parametrizada.
- **`tools/RakionServer.OracleDiff`** — exemplar (records imutáveis, `--self-test`, sem duplicação).
- **`World/Network/UdpGameplay.Process`** — bounds-check exemplar (modelo a replicar no `PacketReader`/`BrokerLink`).
- **`Handlers/LoginHandler`** — early-return, parse capado, loga o fluxo de auth.

> Padrão recorrente: a **fundação certa já existe no repo** (`Common.IniFile`, `PacketReader`,
> classes de constantes em `Protocol.cs`, parse seguro em `UdpGameplay`). O débito estrutural maior
> está hoje em `WorldServer`, `WorldDatabase`, `Field` e nos partials antigos de combate.
