# Handoff — Sistema de Bot + Engine Headless (OpenRakion)

> **Data:** 2026-06-25 · **Sessão:** awesome-jennings-02d24a
> Projeto de **preservação/interoperabilidade** de jogo descontinuado (binários do autor,
> cliente offline próprio). RE e ajustes têm fim de compatibilidade do cliente offline.

Este arquivo é o estado completo da frente "bot que joga no stage", para retomar a qualquer momento.

---

## 1. Objetivo (pedido original, verbatim)

> "adicionar um sistema de bot funcional, onde o usuário pode clicar num botão chamado adicionar
> bot e entrar um bot na sala que ele está criando e **poder jogar contra ele**. O bot deve ter uma
> **inteligência para combate**. Quando o último player sair da sala ou acabar os rounds, a volta à
> criação da sala deve aparecer apenas quem estava lá e não os bots. Caso ele queira de novo, deve adicionar."

### 6 componentes e status in-game
| # | componente | status |
|---|---|---|
| 1 | botão nativo "Add Bot" clicável | ✅ validado in-game |
| 2 | bot entra na sala (slot RED/BLUE, nome+level) | ✅ validado in-game |
| 3 | bot aparece no stage (avatar 3D) | ✅ validado in-game (spawn 0x4b) |
| 4 | **bot joga/se move + IA de combate** | ❌ **FALTA** (é o foco atual) |
| 5 | limpeza: bots somem no fim; só humanos voltam | ✅ validado in-game |
| 6 | re-adicionar funciona | ✅ validado in-game |

**5 de 6 prontos.** O componente 4 (movimento + combate) é o que falta.

### VEREDITO do headless (2026-06-25)
A engine **roda headless** (load/init/netcode/socket — H1/H2/H3-base provados; `StartPeerToPeer` chega no
world-load). MAS **carregar um stage headless é inviável** sem destravar a ponte XFS proprietária do Rakion:
o `.wld` vem do `LevelsSV.xfs` via `PMReaderLib.dll` (**opaco**, sem exports nomeados; a engine nativa só usa
`.gro`), e no fluxo cold-start o arquivo nem é lido (stream stub de 48 bytes, buffer não-mapeado → AV). As
level-archives são montadas/precacheadas pelo jogo real via o menu "Reading levels directory" + `CLevelScriptor`,
passo que o host headless pula. **Caminho recomendado p/ o bot ANDAR: cliente REAL como bot** (2º `rakion.exe`
usa a ponte XFS que já funciona; dirigir por injeção de DLL, chamando as funções in-process da engine com o
mundo já carregado). Todo o asset headless (`engine_host.cpp`, a RE, os endereços) fica reusável. Detalhe na
memória [[headless-engine-host]].

### Receita do pivot "cliente-real-como-bot" (RE feita, pronta p/ build)
O 2º `rakion.exe` carrega o mundo nativamente (sem o muro XFS). A IA dirige o **player LOCAL** dele por
injeção de DLL — **sem simular teclado**, setando a ação direto:
- `_pNetwork` global; **`GetLocalPlayer(_pNetwork)`** @`0x360efe30` (= `*(_pNetwork+0x2c)`) → `CPlayerSource*` local.
- **`CPlayerSource::SetAction(const CPlayerAction&)`** (export `?SetAction@CPlayerSource@@QAEXABVCPlayerAction@@@Z`)
  + **`CPlayerSource::SendAction()`** (`?SendAction@CPlayerSource@@QAEXXZ`). `CPlayerAction` ctor
  `??0CPlayerAction@@QAE@XZ`; layout = o **corpo do 0x30a já revertido** (dt, actState, translação x/y/z, rotação,
  botões, aim) — ver `BotMovement.EncodeActionBody`.
- Por-tick (~50ms): a engine re-colhe a ação do input a cada tick, então injetar via **`_pInput` (CInput*,
  `?_pInput@@3PAVCInput@@A`) setando os eixos de movimento** OU hookando o gather é mais robusto que só `SetAction`
  (que o tick seguinte sobrescreve). A IA já existe em `WorldServer.BotAi` (alvo/cadência) — alimentar por IPC ou
  embutir na DLL. A engine emite o `0x30a` ao host (humano) nativamente → bot anda/luta sem muro.
- FALTA p/ shippar: conta de bot + orquestrar o launch/auto-join do 2º cliente + a DLL de injeção + **teste in-game
  com o usuário**. Mudança de arquitetura (cliente-puppet vs bot server-authoritative) — pede aval do usuário.

---

## 2. Por que o componente 4 é difícil — e a solução (headless)

**O muro (cravado em 3 capturas + RE):** mover o avatar de um peer na Serious Engine exige um
**PEER REGISTRADO** na sessão de jogo da engine. O servidor sintetizar/forjar os frames de movimento
(0x30a) **não** funciona: o corpo do connect de sessão (TAGV/STATEDELTA/CRC/SEQ_ADDPLAYER) é
**off-wire** (montado/consumido dentro da pilha da engine, nunca trafega no fio capturável).

**A solução:** hospedar a **própria `engine.dll`** (que É a Serious Engine 1 open-source da Croteam)
num processo .NET x86 controlado — uma instância **headless** que age como o 2º peer **real**. Ela faz
o session-connect nativo (incl. o corpo off-wire) e o host registra o bot. O servidor .NET continua só
como **broker de endereço + relay UDP**, mantendo o design server-authoritative.

### Milestones do headless (H1–H5)
| | milestone | status |
|---|---|---|
| H1 | `engine.dll` carrega+inicializa standalone (x86, base fixa 0x36000000) | ✅ **provado** |
| H2 | `SE_InitEngine` roda headless → netcode (`_pNetwork`) viva | ✅ **provado** |
| H3-base | init dedicated (no-render) + `PrepareForUse` → **socket de rede ATIVO** | ✅ **provado** |
| H3-join | `JoinSession_t`/`AddPlayer_t` contra host vivo → peer registra | 📐 receita pronta, **falta integrar** |
| H4 | host aplica o 0x30a do bot → avatar anda | ⏳ pendente |
| H5 | bridge WorldServer.BotAi (cérebro) ↔ engine-host (corpo) + detecção de hit | ⏳ pendente |

---

## 3. Locais dos arquivos

### 3.1 Código do servidor .NET (no repo — `openrakion/`)
| arquivo | papel |
|---|---|
| `server/RakionServer/src/RakionServer.World/WorldServer.BotAi.cs` | IA + decisão de combate (cérebro do bot, server-side) |
| `server/RakionServer/src/RakionServer.World/WorldServer.BotRoster.cs` | bot no slot da sala (0x38) + spawn no stage (0x4b) |
| `server/RakionServer/src/RakionServer.World/WorldServer.BotPeer.cs` | mini-peer (transporte) — handshake/brokering |
| `server/RakionServer/src/RakionServer.World/Network/BotMovement.cs` | codec dos frames de spawn/movimento/ataque (0x4b/0x30a/0x30f/0x311) |
| `server/RakionServer/src/RakionServer.World/Domain/BotPlayer.cs` | domínio do bot (patrulha, estado) |
| `server/RakionServer/src/RakionServer.Peer/` | slice mini-peer (codec netcode SE1, **97 testes golden**) |
| `server/RakionServer/src/RakionServer.EngineHost/` | **host headless da engine.dll** (NOVO; x86 self-contained) |
| `server/RakionServer/tests/RakionServer.World.Tests/{BotMovementTests,PeerCodecTests,PeerStreamTests}.cs` | testes golden |

### 3.2 Binários e dados do jogo (FORA do repo — `Desenvolvimento\Rakion\`)
| caminho | conteúdo |
|---|---|
| `C:\Users\joaop\Desenvolvimento\Rakion\rakion-final\Bin\engine.dll` | a engine (SE1), base 0x36000000, 8736 exports C++ |
| `C:\Users\joaop\Desenvolvimento\Rakion\rakion-final\Bin\` | `rakion.exe`, `msvcr71.dll`/`msvcp71.dll` (VC7.1) + todas as DLLs do jogo |
| `C:\Users\joaop\Desenvolvimento\Rakion\rakion-final\` | **data-root**: os `.xfs` (Classes/Data/Scripts/Levels…) — é a CWD que a engine usa |

### 3.3 Artefatos de RE (`Rakion\rakion-work\ghidra-proj\`)
| arquivo | conteúdo |
|---|---|
| `engine_text.asm` | disasm completo do `.text` da engine.dll (39 MB; grep por faixa de endereço) |
| `peer_registration_re.out.txt` | gates do 0x30a (envio @0x3610cd20, aplicação @0x3610d889) |
| `netcode_peer_re.out.txt` | transporte 0x30a/0x0304/0x0305 = game-stream da engine |
| `minipeer_blueprint.out.txt` | máquina S0..S10 do handshake de sessão |
| `p2p_handshake_decode.out.txt` | prova do corpo off-wire (histograma máx 31B/frame) |
| `p2p_direct_channel_re.out.txt` | canal P2P-direto 2300+ |
| `engine_pe.txt` | `objdump -p engine.dll` (exports). Regenerar: `objdump -p engine.dll > engine_pe.txt` |

### 3.4 Memória persistente (`%USERPROFILE%\.claude\projects\C--Users-joaop-Desenvolvimento-openrakion\memory\`)
- `headless-engine-host.md` — **a fundação headless** (este trabalho; H1+H2+H3-base + receita)
- `bot-movimento-muro-p2p-engine.md` — o veredito do muro off-wire (por que síntese é inviável)
- `engine-eh-serious-engine-open-source.md` — a descoberta-chave (engine.dll = SE1 open source)
- `sistema-de-bots.md` — o sistema de bots completo (5/6 componentes)
- `caminhos-binarios-re-build.md` — caminhos de binários/RE/build
- `re-room-ui-buttons.md` — o botão nativo "Add Bot"

### 3.5 Toolchain
- **dotnet** SDK em `C:\Users\joaop\AppData\Local\Microsoft\dotnet` (set `DOTNET_ROOT`; runtime só x64 instalado → host x86 = self-contained).
- **objdump** (WinLibs mingw64, via winget) p/ exports/disasm.
- **Sem compilador C++ 32-bit** (nem `cl.exe`) → por isso o host é .NET x86 com interop ThisCall/Cdecl.

---

## 4. Receita técnica do headless (provada vs binário)

engine.dll base **0x36000000** (sem ASLR → endereços absolutos valem direto). Chamar exports C++ via
delegates `ThisCall`/`Cdecl` nos endereços; construir `CTString`/etc. pelos ctors exportados.

### Gotchas cravados
1. **`SE_InitEngine(CTString gameID)` zera `_pNetwork` se gameID == ""** (`if(g!="") Init(); else _pNetwork=NULL;`).
   → passar **não-vazio** (`"Rakion"`).
2. **CWD = data-root** (`rakion-final\`, onde estão os `.xfs`) — a engine monta os dados pela CWD, não pelo path do EXE.
3. **`_bDedicatedServer = 1` ANTES do `SE_InitEngine`** → gateia o D3D (`CGfxLibrary::InitAPIs`), init sem render.
4. A engine headless abriu socket na **porta stock 25600**, mas o jogo real usa **2300+** e relaia pelo
   **worldserv :40709** — reconciliar no brokering (decisão de design de H3-join).

### Endereços-chave (VA, base 0x36000000)
| símbolo | VA | nota |
|---|---|---|
| `SE_InitEngine` | `0x36008360` | `?SE_InitEngine@@YAXVCTString@@@Z` (cdecl, 1 arg) |
| `_pNetwork` (ptr) | `0x3636f260` | deref → CNetworkLibrary; `+0x14`=isServer, `+0x18`=CServer, `+0x24`=CSessionState |
| `_pNetwork` (alias) | `0x362ba778` | segundo ptr p/ o mesmo objeto |
| `_cmiComm` (obj) | `0x362bcc40` | endereço = `this` direto de CCommunicationInterface |
| `_bDedicatedServer` | `0x362a0680` | int; =1 antes do init → no-render |
| `_pTimer` (ptr) | `0x362acc78` | deref → CTimer (pump) |
| `PrepareForUse(int,int)` | `0x360f8450` | `this=_cmiComm`; (useNet, client) → HOST(1,0) JOINER(1,1) |
| `StartProvider_t` | `0x360feed0` | `this=_pNetwork`; arg CNetworkProvider& |
| `EnumNetworkProviders` | `0x360ff480` | `this=_pNetwork`; arg CListHead& |
| `StartPeerToPeer_t` | `0x360f4380` | host: (CTString name, CTFileName world, ULONG flags, long maxPl, int waitAll, void* props2048) |
| `CWorld::Load_t` | `0x360d6d70` | chamado dentro do StartPeerToPeer (world-load = risco-mestre) |
| `JoinSession_t` | `0x360f5960` | joiner: (CNetworkSession&, long ctLocalPlayers, CTFileName) |
| `CNetworkSession::ctor(CTString,long)` | `0x360febd0` | `+0x08`=ns_strAddress (endpoint do host) |
| `Start_t` (CSessionState) | `0x3610ec10` | branch server/client por `_pNetwork+0x14` |
| `Start_AtClient_t` | `0x3610ea00` | handshake do joiner (REQ_CONNECTREMOTE 7 → … → SEQ_ADDPLAYER 22) |
| `AddPlayer_t` | `0x360f3eb0` | `this=_pNetwork`; (CPlayerCharacter&) → CPlayerSource* (registra o avatar) |
| `MainLoop` | `0x360f58b0` | `this=_pNetwork`; pump por frame |
| gate ENVIO 0x30a | `0x3610cd20` | SessionStateLoop: exige `GetSessionState()[0]!=0` |
| gate APLICAÇÃO 0x30a | `0x3610d889` | HandleMessage case 0x30a: exige `GetPlayer(slot)!=NULL` |

### Ctors úteis (exports)
`??0CTString@@QAE@PBD@Z` (const char*), `??0CTString@@QAE@XZ` (default), `??1CTString@@QAE@XZ` (dtor),
`??0CTFileName@@QAE@PBD@Z`, `??0CNetworkSession@@QAE@ABVCTString@@J@Z`, `??0CPlayerCharacter@@QAE@XZ`.

---

## 5. Como buildar/rodar o host headless

O projeto `RakionServer.EngineHost` **não** entra na `.sln` principal (x64). Builda standalone:

```bash
export DOTNET_ROOT="/c/Users/joaop/AppData/Local/Microsoft/dotnet"
DN="$DOTNET_ROOT/dotnet.exe"
PROJ="server/RakionServer/src/RakionServer.EngineHost"
"$DN" publish "$PROJ/RakionServer.EngineHost.csproj" -c Release -r win-x86 --self-contained true -o "$PROJ/out"
# rodar (arg1 = Bin/ da engine; arg2 = modo):
"$PROJ/out/RakionEngineHost.exe" "C:\Users\joaop\Desenvolvimento\Rakion\rakion-final\Bin" net "Rakion"
```

**Modos:** `map` (mapeia sem init) · `full` (DllMain+ctors) · `init` (SE_InitEngine → netcode up) ·
`net` (dedicated + PrepareForUse → socket ativo). **Próximos:** `join` (H3) etc.

Resultado provado do modo `net` (a engine cuspiu o próprio log):
```
[net] PrepareForUse(useNet=1, client=0) retornou 1  -> socket ATIVO headless ***
Initializing TCP/IP... opening as server, winsock opened ok, port: 25600
```

---

## 6. Próximos passos

1. **H3-join (joiner):** `PrepareForUse(1,1)` → `CNetworkSession(endereçoDoHost)` → `JoinSession_t(ns,1,world)`
   → `AddPlayer_t(bot)` + pump loop (`_pTimer->HandleTimerHandlers` + `MainLoop` a cada ~50ms).
2. **Glue no .NET server:** brokerar o peer headless pra dentro da sessão do host; **reconciliar porta
   25600 stock vs 2300+/relay :40709** (decisão de design).
3. **Teste de integração:** stack rodando + cliente humano num stage → o peer headless entra → avatar anda.
   (Precisa das mãos do usuário: rodar 1 cliente + entrar num stage.)
4. **H4/H5:** confirmar o host aplicando o 0x30a (avatar anda) → ligar a IA de combate (alvo/cadência já
   existem em `WorldServer.BotAi`) + detecção de hit do humano → `BotTakeDamage`.

### Riscos/incertezas marcados
- **world-load headless** (o joiner faz `CWorld::Load_t` do stage do host): a SE1 tem dedicated-server que
  carrega mundo sem render, então deve passar; falta confirmar.
- **porta/relay**: a engine headless usa stock 25600; o jogo real relaia por :40709 — apontar o JoinSession certo.
- **JoinSession_t arg2/arg3** (assinatura Rakion difere da stock): confirmar significado (hipótese arg2=ctLocalPlayers=1).

---

## 7. Snapshot do código do host (`RakionServer.EngineHost/Program.cs`)

> Estado provado (H1+H2+H3-base). O `.cs` vive em `server/RakionServer/src/RakionServer.EngineHost/`.
> Incluído aqui para o handoff ser autossuficiente. (Ver o arquivo no repo para a versão viva.)

A versão completa e versionada está no projeto. Os pontos-chave do interop:
- `LoadLibraryEx` com `LOAD_WITH_ALTERED_SEARCH_PATH` (resolve `msvcr71`/`msvcp71` do Bin/).
- `SetErrorMode` (mata caixas de erro) + `SetDllDirectory(Bin)` + `SetCurrentDirectory(data-root)`.
- delegates: `[ThisCall]` p/ ctors/métodos, `[Cdecl]` p/ `SE_InitEngine`; `CTString` = struct de 1 `IntPtr`.
- data-exports (`_pNetwork`, `_cmiComm`, `_bDedicatedServer`): `GetProcAddress` devolve o endereço do
  global; ptr (`PAV...`) deref uma vez, objeto (`V...`) usa o endereço como `this`.
