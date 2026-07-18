# Auditoria de qualidade de código — OpenRakion

> 2026-07-16. Auditoria estrutural do servidor .NET (`server/RakionServer/`) + scripts.
> Contexto respeitado: comentários `FUN_xxxx`/offsets/MITM são **documentação de RE** (manter);
> AES-128-ECB é a cifra **intencional** do jogo; credenciais de amostra (`root/123456`, `test/test`)
> são abertas de propósito (servidor pessoal offline). Esses itens **não** são apontados como defeito.
>
> Gates derivados desta auditoria estão em [`CLAUDE.md`](../../CLAUDE.md#code-quality-gates).

## Panorama — arquivos acima do alvo (~400 linhas)

| Arquivo | Linhas | Excesso | Natureza |
|---|---:|---:|---|
| `Broker/Systems.cs` | 1797 | 4.5x | god-file decompilado; 790 ln são framework `Ini`/`Profile` |
| `World/Network/ClientSession.cs` | 1032 | 2.6x | socket + replay + handlers de shop/PU + god-object de estado |
| `World/Database/WorldDatabase.cs` | 669 | 1.7x | repositório monolítico, mas **coeso e limpo** |
| `World/WorldServer.cs` | 607 | 1.5x | host de infra **+ motor de partida** misturados |
| `World/Network/WorldHandlers.ReconCombatB.cs` | 516 | 1.3x | seat + relay de combate |
| `World/Domain/Field.cs` | 506 | 1.3x | domínio **+ wire-codec** misturados |

## Plano priorizado

### P0 — Remover código morto

- ✅ A tabela World foi achatada: `WorldHandlers.Generated.cs`, `RegisterGenerated`, `Stub`,
  `StubGates`, `ReconStub`, `RoomMgmt.cs`, `RoomSettings.cs` e os aliases incompatíveis já foram
  removidos. `WorldHandlers.cs` aponta diretamente para os delegates finais.
- ✅ **Aliases privados sem chamador**: removidos após auditoria cruzada da tabela canônica e dos
  interceptores por estado; nenhum método `Op_*` privado permanece com apenas a própria declaração.
- **`ClientSession.cs`**: `public SendInventoryList()` (632-675) sem call-site; `const DiagEmptyInventory=false` + seus blocos (diagnóstico já concluído).
- **`WorldDatabase.cs`**: `LoadCharactersAsync`, `LoadClanAsync`, `InsertUserItemAsync` — zero chamadores (~74 ln).
- **Broker**: `ServerListener.cs` inteiro (96 ln, não referenciado por `Main`); `Systems.CheckServerExpired` (corpo vazio); branch `extip` em `Program.cs:47` (descarta o resultado); overloads-fantasma de `PacketWriter` (`Word`/`WordInt`, `DWord`/`DWordInt`, `String`/`HexString` idênticos).
- **`web/launcher_web.py`** — **morto**: o README já diz que o `.LauncherWeb` (.NET) "aposentou" este arquivo; é reimplementação byte-a-byte do `LauncherWeb/Program.cs`. Mover para `legacy/` ou deletar.
- **`tools/difftest.py`** — provavelmente superseded pelo `RakionServer.OracleDiff` (.NET). Confirmar e remover.

### P1 — Quebrar os god-files (estrutura)
- ✅ O antigo `WorldHandlers.Generated.cs` foi dividido em partials por domínio e eliminado; a
  tabela canônica fica em `WorldHandlers.cs`.
- **`Broker/Systems.cs`** → split físico (preservando bytes do wire): `ServerRegistry`, `ServerInfo`, `Network/FrameDecoder`, `Network/TcpServer`, `Network/ClientSession`, `Network/PacketReader|Writer`. **Apagar o framework `Ini`/`Profile` (790 ln)**: é Windows-only (`WritePrivateProfileString` via P/Invoke → quebra o Docker/Linux que o README anuncia) e **duplica `Common/IniFile.cs`** — usar o `Common.IniFile`.
- **`ClientSession.cs`** → ✅ **replay eliminado** (2026-06-17): os `_rNN` da cadeia lobby→canal→sala→stage viraram síntese pura em `LobbyFrames` (golden-testada byte-a-byte); `OracleReplay.cs`→`LobbyFlow.cs` (só fluxo/dispatch); o `oracle_*.bin` do login já fora p/ `LoginCharListWriter`. Resta extrair `InventoryProtocol`/`ShopSession` e agrupar o god-object de estado em `CharProgress`/`FieldState`/`Wallet`.

### P2 — Separar domínio de infra (regra de negócio fora de I/O/rede)
- **Economia**: compra/venda de storage, Power User e Stage/PvE usam casos de uso no
  `WorldServer` e transações idempotentes no `WorldDatabase`. O prêmio-base de Stage ainda é
  reportado pelo cliente sob os limites originais; falta torná-lo server-calculated.
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
- **Crédito de gold/exp a partir de input do cliente**: `GamePointRules` replica `FUN_0041CF80`
  (`1500/500` stage, `115/160` Deathmatch/Boss, `90/70` Team Death e limites de Golem por
  rodada), mas os valores continuam reportados pelo cliente como no original. **[MÉDIA, legado]**
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

> Padrão recorrente: a **fundação certa já existe no repo** (`Common.IniFile`, classes de constantes
> em `Protocol.cs`, parse seguro em `UdpGameplay`). O débito estrutural maior está no Broker e nos
> partials antigos de combate/`ClientSession` crescidos durante a iteração de RE.
