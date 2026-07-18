# Auditoria de qualidade de código — OpenRakion

## Addendum 2026-07-18 — pós-split + slice OpenGuard

> O panorama de 2026-06-14 abaixo é **histórico**: a dívida dos god-files foi QUITADA em
> 2026-06-17 (ver CLAUDE.md) — `WorldHandlers.Generated.cs` 2692→125, `Broker/Systems.cs`
> 1797→266, `ClientSession.cs` 1032→387. Estado atual medido:

| Arquivo | Linhas | Status |
|---|---:|---|
| `World/Database/WorldDatabase.cs` | ~~907~~ **243** | ✅ **split EXECUTADO** (2026-07-18): + `.Schema` (166) / `.Inventory` (288) / `.Progress` (247) |
| `World/WorldServer.cs` | ~~841~~ **474** | ✅ **split EXECUTADO** (2026-07-18): + `.Match` (257, motor de partida/progressão) / `.Items` (135, refino/catálogo) |
| `World/Security/*` (OpenGuard, 6 arquivos) | 40–159 | ✅ dentro do alvo |

Split feito por movimento mecânico em `partial class` (convenção do repo), zero mudança de
comportamento — build limpo + 48 testes verdes. Maiores arquivos do repo após o split:
`ReconCombatB.cs` (515) e `Generated.Room.cs` (505), dentro do sinal (<600). A extração do
motor de partida para um serviço `MatchEngine` próprio (separação domínio×infra, P2 de 06-14)
continua válida como evolução — o `partial .Match` já isola o código; o passo seguinte é
mover o estado (`_fieldStatusBeat`/`_levelCurve`) junto.

**OpenGuard (slice `World/Security/`, auditado no ship 2026-07-18):** 3 achados corrigidos no
mesmo dia — (1) rate-limit de opcodes movido para o ponto único `ClientSession.DispatchAsync`
(cobria só o dispatch genérico; login 0x0C e cadeia de lobby escapavam); (2) chave do coalesce
do `DbViolationSink` inclui a conta (slot é reusado entre sessões — `hits` vazava);
(3) decaimento de score (`ScoreDecayPerMin`) — sem ele, sessão longa acumulava Low até
falso positivo com `EnforceKick`. Limitações conhecidas e aceitas: hash de cliente é
auto-reportado (documentado); flood UDP cobre só o ramo de relay (o único com amplificação).

---

> 2026-06-14. Auditoria estrutural do servidor .NET (`server/RakionServer/`) + scripts.
> Contexto respeitado: comentários `FUN_xxxx`/offsets/MITM são **documentação de RE** (manter);
> AES-128-ECB é a cifra **intencional** do jogo; credenciais de amostra (`root/123456`, `test/test`)
> são abertas de propósito (servidor pessoal offline). Esses itens **não** são apontados como defeito.
>
> Gates derivados desta auditoria estão em [`CLAUDE.md`](../CLAUDE.md#code-quality-gates).

## Panorama — arquivos acima do alvo (~400 linhas)

| Arquivo | Linhas | Excesso | Natureza |
|---|---:|---:|---|
| `World/Network/WorldHandlers.Generated.cs` | 2692 | 6.7x | god-file de ~50 handlers de domínios distintos |
| `Broker/Systems.cs` | 1797 | 4.5x | god-file decompilado; 790 ln são framework `Ini`/`Profile` |
| `World/Network/ClientSession.cs` | 1032 | 2.6x | socket + replay + handlers de shop/PU + god-object de estado |
| `World/Database/WorldDatabase.cs` | 669 | 1.7x | repositório monolítico, mas **coeso e limpo** |
| `World/WorldServer.cs` | 607 | 1.5x | host de infra **+ motor de partida** misturados |
| `World/Network/WorldHandlers.ReconCombatB.cs` | 516 | 1.3x | seat + relay de combate |
| `World/Domain/Field.cs` | 506 | 1.3x | domínio **+ wire-codec** misturados |
| `World/Network/WorldHandlers.ReconRoomA.cs` | 395 | — | **arquivo inteiro morto** (ver P0) |

## Plano priorizado

### P0 — Remover código morto (~700 linhas, baixo risco)
Verificar cada item (grep dos call-sites) antes de apagar; vários foram confirmados sem chamadores pelos auditores.

- **`WorldHandlers.ReconRoomA.cs` inteiro (395 ln)** — a tabela `Build()` (`WorldHandlers.cs:82`) aponta 0x29/0x2c/0x31/0x32/0x33/0x35/0x38 para os handlers **antigos** (`Op_RoomSetMode`/`RoomSettings.cs` etc.), não para os `_Recon`. Há **duas implementações paralelas** do mesmo RoomSet* — golden source violado. Decidir: ligar a tabela aos `_Recon` e apagar os antigos, **ou** apagar `ReconRoomA.cs`. Não manter os dois.
- **`Generated.cs`**: `Op_FieldGameAction`, `Op_FieldUnitPickup`, `Op_FieldPairUpdate`, `Op_FieldSlotAction`, `Op_GamePointSettle` (0x50 é `Op_0x50_Recon`), `FieldRoleSlotA/B` — não estão em `RegisterGenerated()` nem são chamados (~470 ln). Ao removê-los, revisar os campos `Field*` órfãos em `ClientSession` (28-100).
- **`ClientSession.cs`**: `public SendInventoryList()` (632-675) sem call-site; `const DiagEmptyInventory=false` + seus blocos (diagnóstico já concluído).
- **`WorldDatabase.cs`**: `LoadCharactersAsync`, `LoadClanAsync`, `InsertUserItemAsync` — zero chamadores (~74 ln).
- **`Recon.cs` `Op_0x2E_Recon`** — inalcançável (`Generated.cs:22` sobrescreve 0x2e em runtime).
- **Broker**: `ServerListener.cs` inteiro (96 ln, não referenciado por `Main`); `Systems.CheckServerExpired` (corpo vazio); branch `extip` em `Program.cs:47` (descarta o resultado); overloads-fantasma de `PacketWriter` (`Word`/`WordInt`, `DWord`/`DWordInt`, `String`/`HexString` idênticos).
- **`web/launcher_web.py`** — **morto**: o README já diz que o `.LauncherWeb` (.NET) "aposentou" este arquivo; é reimplementação byte-a-byte do `LauncherWeb/Program.cs`. Mover para `legacy/` ou deletar.
- **`tools/difftest.py`** — provavelmente superseded pelo `RakionServer.OracleDiff` (.NET). Confirmar e remover.

### P1 — Quebrar os god-files (estrutura)
- **`WorldHandlers.Generated.cs`** → seguir a convenção `partial` já usada no resto: `…Generated.Field.cs`, `…Generated.Room.cs`, `…Generated.Shop.cs` (compra/venda/alocação + helpers `SellBoxSlot`/`SellPriceOf`/`SendShopError`), `…Generated.GameResult.cs`, `…Generated.Verify.cs`. `RegisterGenerated()` fica como índice.
- **`Broker/Systems.cs`** → split físico (preservando bytes do wire): `ServerRegistry`, `ServerInfo`, `Network/FrameDecoder`, `Network/TcpServer`, `Network/ClientSession`, `Network/PacketReader|Writer`. **Apagar o framework `Ini`/`Profile` (790 ln)**: é Windows-only (`WritePrivateProfileString` via P/Invoke → quebra o Docker/Linux que o README anuncia) e **duplica `Common/IniFile.cs`** — usar o `Common.IniFile`.
- **`ClientSession.cs`** → ✅ **replay eliminado** (2026-06-17): os `_rNN` da cadeia lobby→canal→sala→stage viraram síntese pura em `LobbyFrames` (golden-testada byte-a-byte); `OracleReplay.cs`→`LobbyFlow.cs` (só fluxo/dispatch); o `oracle_*.bin` do login já fora p/ `LoginCharListWriter`. Resta extrair `InventoryProtocol`/`ShopSession` e agrupar o god-object de estado em `CharProgress`/`FieldState`/`Wallet`.

### P2 — Separar domínio de infra (regra de negócio fora de I/O/rede)
- **Economia** (compra `Op_RoomMemberQuery`, venda `SellBoxSlot`, `HandleBuyPowerUser`, `CreditSoloResult`) vive em handlers de rede/sessão — mover para um `ShopService`/`Economy` de domínio; o handler só serializa.
- **Motor de partida** (`MatchTick`/`SettleMatch`/`GrantExp`/clocks) em `WorldServer` → extrair `MatchEngine`.
- **Wire-codec em entidade de domínio**: `Field.Build0x48/49/4a`/`BuildMatchEnd`/`SerializeListEntry` → mover para um `FieldWireCodec`/camada Network; `Combat_*` de `ReconCombatA` viram métodos de `Field`/`PlayerRec`.

### P3 — Dedup + segurança por construção
- **Gate de field duplicado em ~40 handlers** (`if (!(u.InField && u.FieldSecondary)) {Disconnect} if (Status != …) {Disconnect}`) — o helper `ReconGate` já existe em `Recon.cs:86` mas não é usado uniformemente; criar `ReconGate2(ctx, discNoField, discBadStatus)` e aplicar. Padronizar `UserStatus.InField` em vez de `0x03` cru.
- **`PacketReader` seguro por construção** — ver Segurança abaixo.
- **`CreditMatchReward`** duplicado entre 0x50/0x53; `MapCharacter`/`StatColumns`/`WithConnection` em `WorldDatabase`; `LogConsole`↔`LogDebug` no Broker (~70 ln copy-paste).
- Renomear handlers cujo nome mente: `Op_RoomMemberQuery`→`Op_ShopBuy`, `Op_GroupMemberInfo`→`Op_InventorySell`, `Op_RoomRosterSync`→`Op_InventoryLeave` (manter o opcode no comentário).

## Bugs latentes (não são só estilo — corrigir)

- **`PacketReader` sem bounds-check** (`Common/PacketReader.cs`): `Byte/Int16/Int32/UInt32/String/Bytes` leem sem validar `Remaining`. Vários handlers leem **sem `CanRead`** (ex.: `ReconCombatB.cs:107,418,479`). Cada `Dispatch` está em try/catch, então não derruba o processo, mas um frame curto/forjado vira `IndexOutOfRange` em vez de DISC limpo. **Tornar o reader seguro por construção** (cada primitivo valida e lança `EndOfPacket`) fecha a classe inteira de bugs. **[ALTA]**
- **Opcode 0x4a com 2 layouts de bytes** em 4 emissores (`ReconCombatB.cs:399`, `ReconRoomB.cs:121`, `Field.cs:274`, `Field.cs:481`): `[LastRoundWinner,WinnerSide,Wins0,Wins1]` vs `[WinnerSide,Wins0,Wins1,LastRoundWinner]`. No máximo um está certo no fio. Consolidar em `Field.Build0x4a()`. **[ALTA]**
- **`MaxPlayers = (byte)capacity`** (`WorldServer.cs:66`) trunca capacidades >255 (`capacity` é `ushort`, até 1210). **[MÉDIA]**
- **`field.State` sem null-check** em `Op_FieldCreateRoomEntry` (`Generated.cs:934`) — `field` vem de `GetField` (pode ser null) → NRE. **[MÉDIA]**
- **`HandleBuyPowerUser`** (`ClientSession.cs:811`) debita `Cash` e dispara 2 `Async` fire-and-forget **sem rollback** se o DB falhar (a compra de item reverte; aqui não). **[MÉDIA]**
- **`EchoGameplayUdp`** lê `BitConverter.ToUInt32(pkt,19)` (precisa `Length>=23`) com guarda só `>=21` (`BrokerLink.cs:106`). **[MÉDIA]**
- **Buddy CDs com 3 fontes de verdade**: `BuddyProtocol.cs:36` tem `RET_SET_NICK=0x3151`/`RET_GROUP_GETLIST=0x3152`, mas o `Dispatch` responde `0x3101`/`0x3151` (literais). **Os literais são os validados in-game** (a `OnMsg` da Buddy2.dll trata `0x3101`=RET_SET_NICK) — então as **constantes `RET_SET_NICK`/`RET_GROUP_GETLIST` estão erradas/legadas** e devem ser corrigidas para casar com os literais (golden source único). **[MÉDIA]**

## Segurança (real — fora os itens intencionais)

- **Parse de input externo sem bounds-check** — ver bug `PacketReader` acima; também `BrokerLink.HandlePacketAsync` (UDP, `r.String()` com `len` do fio). **[ALTA]** (mitigado por try/catch, mas é a classe de bug a fechar).
- **Senhas de conta em texto puro** no DB (`LauncherWeb/Program.cs:38`, `AdminDb.CreateAccount/SetPassword`). **Imposto pela fidelidade ao cliente** (o launcher manda `pass` como hex de latin1, comparação direta) — não corrigível sem mexer no cliente. **Registrar como risco conhecido**: um dump do DB expõe todas as senhas. **[MÉDIA, aceito]**
- **Crédito de gold/exp a partir de input do cliente**: `CreditSoloResult` e `ValidateGamePoints` (teto fixo 1M) creditam+persistem valores reportados pelo cliente. Aceitável no escopo offline single-user, mas é a superfície de trapaça mais sensível — merece o RE fiel do anti-cheat. **[MÉDIA, aceito]**
- **Vazamento de credencial em log**: `ClientSession.ReceiveLoopAsync` loga RX em hex (`:168`) e cada opcode/data (`:199`) em nível **Info** — despeja payload de login/hash no log. Rebaixar para Debug e/ou redigir o login. **[MÉDIA]**

## O que está bem (referência de qualidade)

Código novo, idiomático e limpo — usar como padrão ao refatorar o resto:
- **`RakionServer.Buddy`** — escrito do zero, DTO de frame explícito, logs nos fluxos certos.
- **`RakionServer.Admin`** (incl. `AdminDb`) — **SQL 100% parametrizado, sem injection**; records/DTOs; vertical slice.
- **`RakionServer.LauncherWeb`** — minimal-API enxuta, parametrizada.
- **`tools/RakionServer.OracleDiff`** — exemplar (records imutáveis, `--self-test`, sem duplicação).
- **`World/Network/UdpGameplay.Process`** — bounds-check exemplar (modelo a replicar no `PacketReader`/`BrokerLink`).
- **`Handlers/LoginHandler`** — early-return, parse capado, loga o fluxo de auth.

> Padrão recorrente: a **fundação certa já existe no repo** (helpers `ReconGate`/`SendField`, `Common.IniFile`, classes de constantes em `Protocol.cs`, parse seguro em `UdpGameplay`) — falta **adotá-la de forma uniforme**. O débito está concentrado no `Broker` (porte de decompilação) e no `Generated.cs`/`ClientSession.cs` (crescidos por iteração de RE).
