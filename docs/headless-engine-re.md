# RE complemento — engine.dll headless (carregar um stage sem janela)

> Objetivo: hospedar a `engine.dll` (Serious Engine 1) num processo .NET/C++ próprio que entra na
> sessão do cliente-humano como **peer de rede real**, pra o bot ser tratado pelo host como um 2º
> jogador (colisão/animação/combate/morte **nativos**). Foundation H1+H2 provadas; este doc cobre a
> RE do **muro do world-load headless** e o caminho de furá-lo. Continua [`headless-bot-status.md`].

> **MARCO 2026-06-28 — o headless CARREGA O STAGE.** Com o Atalho B (§7) o `StartPeerToPeer` preencheu o `.wld`
> + 31 texturas/modelos e chegou em **"Starting session / network is on"** — a engine headless HOSPEDA a sessão
> com o mundo carregado. O muro "inviável" de 06-25 CAIU. **Frontier novo (§8):** as classes de entidade
> (`CWorldBase`, `CPlayer`) moram no `EntitiesMP.dll`, que **falha ao init headless** (`LoadLibrary err 1114 =
> DLL_INIT_FAILED`; DllMain retorna FALSE) — DLL empacotada/protegida (carrega no `rakion.exe`, falha fora).

Base: `engine.dll` mapeia na base fixa **0x36000000** (sem ASLR) — todos os endereços abaixo são absolutos.
Host de teste: `server/RakionServer/src/RakionServer.EngineHost/native/engine_host.cpp` (C++ x86, MSVC;
`cl /EHa`). Roda de dentro de `rakion-final/Bin/` com CWD = `rakion-final/` (data-root dos `.xfs`).

---

## 1. Onde travava (o "muro" de 2026-06-25, reaberto e furado em 2026-06-28)

A engine sobe headless (`SE_InitEngine("Rakion")` → `_pNetwork` vivo), o socket abre, mas `StartPeerToPeer_t`
(host) / `JoinSession_t` (joiner) **AVam ao carregar o mundo**. O veredito antigo ("ponte XFS opaca,
não compensa") estava **incompleto** — partia de duas premissas FALSAS, derrubadas agora:

- ❌ "O leitor de XFS é o `PMReaderLib.dll` opaco." → **FALSO.** `PMReaderLib.dll` importa `mscoree.dll`:
  é uma **DLL .NET/gerenciada** (red herring; o objdump nativo não lê os exports managed → pareceu "opaco").
- ❌ "A engine só conhece `.gro`; o `.wld` não é lido." → **FALSO.** A `engine.dll` tem um **leitor de XFS
  NATIVO embutido** (ver §4). O `.wld` É lido nativamente do `.xfs`.

O AV real não era o world-load em si — era uma fase **anterior** (coleta de CRC). Furado com 2 patches (§3),
o world-load passou a **abrir e ler o `.wld`** (§5). O muro agora é mais fundo e específico (§6).

---

## 2. Cadeia de chamada do AV (cravada por stack-walk no SEH)

O `engine_host.cpp` roda `StartPeerToPeer_t` num filtro SEH que dumpa `eip`, os campos do CTStream
(`+0x0C`=buffer `+0x14`=cursor `+0x18`=eof) e **varre a pilha por endereços de retorno na `.text`**
(0x36001000–0x36215000) = a cadeia de chamada. Resolvendo os RVAs contra a export table:

**AV original (antes dos patches), RVA 0x3C803 (helper expect-string em `0x3603C7B0`, lido por `ReadFromText_t`):**
```
StartPeerToPeer_t            (0xF4380)
└ CNetworkLibrary::InitCRCGather        (0xF41E0)   ← coleta de CRC das deps do mundo
  └ CStock_CEntityClass::Obtain_t       (0xE2F80)
    └ CSerial::Load_t                   (0x337D0)
      └ CEntityClass::Read_t            (0x12B4B0)
        └ CTFileName::ReadFromText_t    (0x22CD0)
          └ expect-string @0x3C7B0  AV @0x3C803  (strlen+compara; lê stream vazio: buf page-aligned, eof=buf+0x30)
```
(o `CTStream::ExpectID_t` exportado é outro, `0x3603AFE0`; `0x3C7B0` é o helper que `ReadFromText_t` usa)
→ a `InitCRCGather` percorre as classes/deps e calcula CRC de cada uma; uma `.ecl` vinha com stream vazio.

**AV atual (depois dos patches), RVA 0x3AF88 = `CTStream::PeekID_t`:**
```
StartPeerToPeer_t                       (0xF4380 +0x21c)
└ CWorld::Load_t                        (0xD6D70 +0x81)   ← ABRIU o ko2.wld via Open_t ✅
  └ CNetworkLibrary::CheckVersion_t     (0xF00D0 +0x24)   ← lê a versão do mundo
    └ CTStream::PeekID_t  AV @0x3AF88   (lê o chunk-ID; cursor numa página não-committed)
```

---

## 3. Os 2 patches que furaram a coleta de CRC (aplicados no `engine_host.cpp`)

A coleta de CRC compara o hash de mundo/deps entre cliente e servidor — **descartável offline**.

| Patch | VA | Bytes | Efeito |
|---|---|---|---|
| `CTStream::GetStreamCRC32_t` → `xor eax,eax; ret` | `0x3603C660` | `33 C0 C3` | neutraliza o gather-loop que lia EOF−buffer bytes (AV deslocou p/ ExpectID) |
| `CNetworkLibrary::InitCRCGather` → `ret` | `0x360F41E0` | `C3` | **pula a coleta de CRC inteira** → `StartPeerToPeer` vai direto ao world-load |

O 2º patch foi o desbloqueio: com a `InitCRCGather` neutralizada, o `StartPeerToPeer` chega no
`CWorld::Load_t`, que chama `CTFileStream::Open_t("LevelsSV\ko2\ko2.wld")` — e o hook de Open_t **logou o
.wld**: o mundo passou a ser ABERTO. `CheckVersion_t` chegou a ler o magic **"BUIV"** (0x56495542, o chunk
de versão que abre todo `.wld`) → confirma que o conteúdo do `.wld` começou a ser lido.

---

## 4. O leitor de XFS NATIVO da engine (a descoberta-chave)

`CTFileStream::Open_t @0x3603E920` resolve o path e tenta, NESTA ordem, achar o arquivo:

1. **`xfsMan::InitXfsFile(CTFileName&)`** `@0x3603E520` → devolve o `xFileManager*` do `.xfs` que contém o arquivo.
2. **`xFileManager::open(const char*, bool, bool)`** `@0x3601B430` → abre o arquivo DENTRO do `.xfs` (devolve `xFile*`).
3. **`CTStream::AllocateVirtualMemory(K)`** `@0x3603B600` → reserva o buffer do stream (tamanho = uncompressed).
4. Fallback **loose**: `fopen`/`fseek`/`ftell`/`fread` (IAT `0x3621544C`=fopen, `0x362154CC`=ftell) — se não estiver no XFS, lê do disco solto.

Namespace completo: **`Xenesis2::Foundation::File::xFileManager`** + singleton **`xfsMan`**. Exports úteis:
- `?init@xFileManager@...@@QAE_NPBD_N@Z` — `xFileManager::init(const char* xfsPath, bool)` (monta um `.xfs`).
- `?open@xFileManager@...` `@0x3601B430` — abre arquivo do `.xfs`.
- `?isActivate@xFileManager@...` — o `.xfs` está montado?
- `?createFileTable@xFileManager@...` — monta a tabela de arquivos (nome→XFSFileInfo, `std::map`).
- `?getFileSize@`/`?getCompSize@`/`?getRealSize@` — tamanhos.
- `?FindXfsFile@xfsMan@@AAEPAVxFileManager@...@@AAVCTFileName@@@Z` — qual `.xfs` tem o arquivo.
- `?setXFS@`/`?resetXFS@` — (des)registra o `.xfs` no manager.

**Conclusão:** a engine LÊ XFS sozinha. Não precisa de `PMReaderLib` nem de reimplementar provider nem de
extrair tudo solto. O que falta é fazer o `xfsMan` **montar os `.xfs`** headless (a init que o cliente real
faz no boot) — ou descobrir por que o read/descompressão pro buffer não completa (§6).

---

## 5. Estado atual: o `.wld` ABRE (do XFS NATIVO), mas o buffer só é RESERVADO — o fill é lazy e não dispara

No AV de `PeekID_t`, o dump do CTStream (`esi`) mostra:
```
S[+0=362162C8(vtbl)  +0xC=03BA0000(buffer)  +0x14=03BA0000(cursor)  +0x18=03BB1871(eof)]
eof - buffer = 0x11871 = 71793 = tamanho EXATO do ko2.wld (uncompressed)
```
**Confirmado com os arquivos loose DELETADOS:** o `Open_t` ainda acha o `ko2.wld` e o tamanho certo (71793) →
veio do **XFS NATIVO** (`xfsMan` achou no `LevelsSV.xfs`), não de loose. Loose não é necessário.

**RE do `Open_t` (0x3603EA9E→0x3603EC13) — caminho XFS:**
```
InitXfsFile(fnm)         → [esi+0x60]=xFileManager*  (se 0 → cai no loose fopen)
open(name,1,0)           → [esi+0x64]=xFile*         (se 0 → loose)
ebp = [[xFile+8]+0x78]   = tamanho UNCOMPRESSED (71793)
AllocateVirtualMemory(round(size))  → reserva buffer em [esi+0xC]
[esi+0x18] = buffer + size           (EOF)
[esi+0x5c] = 1                        (flag: stream lê do xFile/XFS, lazy)
→ FIM. NENHUM read/decompress no Open_t.
```
Idem o caminho LOOSE (fopen/ftell→tamanho→AllocateVirtualMemory→EOF→`[esi+0x5c]=1`→FIM): **só reserva**.
Ou seja o preenchimento é **LAZY** — `CTFileStream` guarda o `xFile` em `[esi+0x64]` e descomprime sob demanda
na 1ª leitura (via `?FileReadAhead_t@CTFileStream@@QAEXJJ@Z`). **Headless esse read lazy não preenche o buffer**,
e o `PeekID_t` lê `[cursor]` DIRETO do buffer reservado-vazio → AV. Falta: descobrir por que o `FileReadAhead`
não dispara/falha headless (o `xFile`/device `xWindowFile`/`xDeviceFile` ou o trigger de buffer).

Campos do CTFileStream cravados: `+0xC`=buffer `+0x14`=cursor `+0x18`=eof `+0x50`=FILE* (loose) `+0x5c`=flag-xfs
`+0x60`=xFileManager `+0x64`=xFile.

---

## 6. Formato XFS (decodificado) + extrator

Necessário pra extração loose E pra pré-preencher buffer via hook. Cravado em `tools/` + `scratchpad/xfs_extract.py`:
- Header do `.xfs`: `[i32 startOff]` no offset 0; em `startOff`: `[u8 zlen][zlib(head)]` então `[u24 zlen][zlib(fileTable)]`.
  `head` = `XFS2` + `[i32 version][i32 count][i32 validation][i32 dataStart]` + tail "The Xenesis2…".
- File table: por arquivo `[name 112B][i32 foff][i32 comp][i32 uncompressed][i32 compressed]`.
- **Bloco de arquivo (multi-chunk):** sequência de chunks de até 64KB, cada um
  `[u16 ucChunk][u8 0x80][u24 zlen][u16 cks][zlib]`. Decodificar = caminhar os streams zlib (achar magic
  `78 9c`, `zlib.decompressobj` lê até `unused_data`, repetir). `decode_block()` reconstrói byte-a-byte
  (validado: `ko2.wld` → 71793 ✓). **NÃO é encriptado.**
- `LevelsSV.xfs` = 30 stages em `levelssv\<nome>\<nome>.wld` (+ `.wld.dat`, `.tbn`, `_minimap.tex`).
  `Classes.xfs` = 271 `.ecl` (incl. `classes\player.ecl`, ~48B = referência de classe).

---

## 7. Próximos passos (frontier)

`?FileReadAhead_t@CTFileStream@@QAEXJJ@Z` `@0x3603BB70` = o read lazy: `eax=[esi+0x54]` (file descriptor, signed);
`if eax<0 → ret (pula o read)`; senão chama `read(fd,…)` `@0x360420C0`. **O caminho XFS do `Open_t` NÃO seta
`[esi+0x54]`** (só `[esi+0x64]`=xFile) → o `FileReadAhead` por-fd não serve o XFS; o fill do XFS descomprime do
`xFile [esi+0x64]` por outro caminho, que não dispara antes do `PeekID` headless.

- **Atalho B — preencher o buffer via hook do `Open_t` ✅ IMPLEMENTADO E FUNCIONOU.** No `engine_host.cpp`:
  `OpenHook`/`OpenWrapperImpl`/`FillBufferFromXfs` — wrapper do `Open_t` (`__fastcall`, chama o original via
  trampolim e depois preenche). No retorno, lê o filename do `CTFileName`, abre `C:\temp\xfs_ext\<path>` (pré-extraído
  via `scratchpad/xfs_extract.py`), `VirtualAlloc(buf, size, MEM_COMMIT)` + `fread` no buffer (`esi+0xC`,
  tam=`esi+0x18−esi+0xC`). **Resultado: o `.wld` + 31 texturas/modelos preenchidos → world-load COMPLETO →
  "Starting session / network is on".** Pré-extraídos pro temp: `LevelsSV`+`Classes`+`TexturesSV`+`ModelsSV`+
  `Textures`+`Models`+`Animations`+`Shaders` (467MB).

## 8. `EntitiesMP.dll` (classes de entidade) headless ✅ RESOLVIDO — world-load COMPLETO
Pós-world-load, instanciar entidades exige as classes C++ (`CWorldBase`, `CPlayer`, ...) do **`EntitiesMP.dll`**.
Dois bloqueadores, ambos diagnosticados por RE runtime (VEH + hook de IAT + dump de export) e resolvidos:

**8.1 `LoadLibrary(EntitiesMP)` → err 1114 = `DLL_INIT_FAILED`. Causa-raiz: DEP, NÃO anti-tamper.**
A `entitiesmp.dll` é **ASPack** (EP `0x354C1001` dentro de `.data`; seção `.adata`; `relocations stripped`,
`DllCharacteristics=0`, sem `.reloc` → base FIXA `0x35000000`). Hipótese de conflito-de-base **REFUTADA**
(VirtualQuery: `0x35000000` está `FREE` o tempo todo e ainda falha). Um **VEH** cravou o fault: **AV (`C0000005`)
no PRÓPRIO entry-point `0x354C1001`** = a 1ª instrução do stub ASPack (`pushad`) sendo **executada**. Isso é **DEP**:
o stub roda em página de **dados** não-executável; o `rakion.exe` (2007) roda **sem DEP**, mas o `engine_host`
(MSVC 2022) tinha **`/NXCOMPAT` ligado por padrão**. **Fix: linkar `/NXCOMPAT:NO`** (DEP off no processo) →
`gamemp.dll`+`EntitiesMP.dll` carregam (handles `0x10000000`/`0x35000000`, err 0). O stub desempacota normalmente
(os ints/AVs em `~0x01C6xxxx` são a auto-descriptografia, tratados pelo SEH do próprio stub).

**8.2 Classes não-encontradas mesmo com a DLL carregada. Causa: handle NULL no class-loader.**
A export table (5267 nomes: `CPlayer`, `CWorldBase_DLLClass`@`0x35392160`, ...) é **restaurada** em memória pelo
unpack — `GetProcAddress(hEnt, "CWorldBase_DLLClass")` funciona. Mas a engine ainda dava "not found". Um **hook de
IAT** na `GetProcAddress` da engine revelou: ela chama **`GetProcAddress(h=NULL, "CWorldBase_DLLClass")`** — handle
**NULL** (e **nunca** chama `LoadLibraryA` p/ a EntitiesMP). No jogo original a EntitiesMP é **import ESTÁTICO** da
`rakion.exe` (protegida) → a engine assume o módulo no próprio exe. Headless não há. **Fix: bridge no `GpaHook`** —
pedido de `*_DLLClass` com handle nulo → resolve contra `EntitiesMP`/`gamemp` já carregadas.

**8.3 AV-write em buffer reservado durante o world-load (serialização). Fix: auto-commit.**
Com as classes registrando, o world-load avançou e bateu em `mov [ecx],eax @0x3601F61E` escrevendo no **cursor de
um CTStream** (`edi+0x14`) numa página **`MEM_RESERVE`** (`0x04xxxxxx`): a engine reserva buffers grandes e commita
sob demanda, mas o commit-on-grow não dispara headless. **Fix: `AutoCommitVeh`** — AV-write em página `RESERVE` →
`VirtualAlloc(MEM_COMMIT)` + `EXCEPTION_CONTINUE_EXECUTION` (só toca `RESERVE`; `FREE` = bug real, deixa crashar).

**Resultado: `StartPeerToPeer RETORNOU OK` — o stage `ko2` carrega COMPLETO (mundo + entidades) headless,
sessão "BotHost" no ar, "network is on".** Os 4 fixes vivem no `engine_host.cpp` (build `/NXCOMPAT:NO` via
`native/build.ps1`, MSVC BuildTools x86). Decisão de host: **o `engine_host.exe` C++ é o host de produção** — o host
.NET (`RakionEngineHost`) NÃO carrega a engine.dll VC7.1 neste ambiente (CLR moderno → err 126).

**Próximo (H3.5→H5):** o host hospeda a SUA sessão; falta unir à sessão de stage do HUMANO (quem é o host SE1 —
broker do world server) + `AddPlayer_t` → H4 (host aplica 0x30a do bot) → H5 (bridge `WorldServer.BotAi` ↔ corpo por
IPC + hit→`BotTakeDamage`).

## 9. Manter a sessão VIVA + protocolo de JOIN (da fonte SE1 open-source, validado)
**9.1 Pump do host ✅ FUNCIONA.** Pela fonte SE1 há DOIS loops: **`TimerLoop`** (registrado no `StartPeerToPeer`
via `AddTimerHandler`; no host roda **`ga_srvServer.ServerLoop()`** = aceita joiners + simula) disparado por
**`CTimer::HandleTimerHandlers`** a **TickQuantum=1/20s (20Hz)**; e **`MainLoop@0x360F58B0`** = loop por-FRAME
(prediction/game-stream, lado-cliente/render). **O `MainLoop` CRASHA headless** (AV-read em `0x3610CDE1`, deref do
global de render `[0x3636f260]`=NULL → virtual call). **Fix: o host dedicado bombeia SÓ `HandleTimerHandlers`** (não
`MainLoop`) → sessão fica **viva e estável** (testado 8s, sem crash). No `engine_host.cpp`: `PumpSEH` a 20Hz.

**9.2 Protocolo de JOIN (modelo escolhido = (a) humano hospeda, bot faz `JoinSession_t`).**
- `JoinSession_t(const CNetworkSession&, INDEX ctLocalPlayers)` — **2 args, SEM CTFileName**. O mundo vem do HOST no
  handshake; o joiner carrega o `.wld`+classes LOCALMENTE (delta-sync) → por isso precisa do world-load headless (já
  resolvido §1–8). `CNetworkSession(const CTString& strAddress)` — só o endereço (`"ip"` ou `"ip:porta"`); ctor
  `(addr,long)` NÃO existe (o `long` que eu via é o `ctLocalPlayers` do JoinSession).
- `Start_t(ctLocalPlayers)` ramifica: server→`_cmiComm.Client_Init_t(0)`+`Start_AtServer_t`; client→
  `_cmiComm.Client_Init_t(addr)`+`Start_AtClient_t(ctLocalPlayers)`.
- **Handshake do cliente** (`Start_AtClient_t`, ordem na wire): `MSG_REQ_CONNECTREMOTESESSIONSTATE` (o **VTAG** das
  capturas: `'VTAG'`+build maj/min+mod+senha+ctLocalPlayers+`ses_sspParams`) → resp traz MOTD+`fnmWorld`+spawnFlags+
  props → **`NET_MakeDefaultState_t`** monta o baseline local → `MSG_REQ_STATEDELTA` → resp comprimido (zlib
  `UnpackStream_t`→`DIFF_Undiff_t`→`Read_t` carrega o estado de entidades) → `MSG_REQ_CRCLIST`/`MSG_REP_CRCCHECK`
  (o **CRC** das capturas) → keepalive. (Logo o `InitCRCGather`/`GetStreamCRC32` que patchei p/ o HOST precisam de
  cuidado no JOINER — o CRC-check é parte do handshake; reavaliar.)
- **`AddPlayer_t(CPlayerCharacter&) → CPlayerSource*`** — chamar DEPOIS de `StartPeerToPeer`/`JoinSession` retornar.
  Internamente `CPlayerSource::Start_t` manda **`MSG_REQ_CONNECTPLAYER`** (serializa o `CPlayerCharacter`) e recebe
  **`MSG_REP_CONNECTPLAYER`** com o `pls_Index` (= o **ADDPLAYER** das capturas). `CPlayerCharacter`: `pc_aubGUID[16]`
  + `pc_strName` + `pc_strTeam` + `pc_aubAppearance[32]`; ctor `(const CTString& name)`.
- Cross-check com as capturas: **VTAG**=connect, **CRC**=CRC-list, **ADDPLAYER**=connect-player. O "corpo off-wire" que
  não capturei é o conteúdo reliable-stream dessas 3 mensagens (no loopback). **Isto fecha o RE do muro off-wire de
  [[bot-movimento-muro-p2p-engine]].**

**9.3 ACHADO do teste 2-processos: o caminho do CLIENTE precisa de gráficos; o do HOST não.** Implementei o modo
`join` (`CNetworkSession`+`JoinSession_t`+pump) e testei 2 `engine_host` locais (1 host + 1 join). O **host** roda
LIMPO (world-load + sessão viva 8s). O **joiner** (`Start_AtClient_t`) CRASHA derefando o **render-global `0x3636f260`**
(NULL headless) em VÁRIAS funções do caminho do cliente (`0x3610CDE1` render-status — patchei jb→jmp @0x3610CDC9; depois
`0x3610305B` em `0x36103040`, e o cluster `0x360FF7xx`). É a UI de progresso de conexão ("Connecting/Receiving") que o
cliente mostra e o servidor não — patchar cada uma é whack-a-mole arriscado (algumas são lógica de estado, não só UI).
**CONSEQUÊNCIA ARQUITETURAL:** o caminho headless-limpo é o **HOST/servidor**. ⇒ modelo **(b) o BOT HOSPEDA a sessão
(headless, limpo) e o CLIENTE HUMANO REAL entra** (o humano tem gráficos → o caminho do cliente funciona pra ele)
**evita 100% o problema** — o bot nunca roda o caminho gráfico do cliente. **DECISÃO DO USUÁRIO 2026-06-28 = modelo (b)**
(escala bem: 1 engine-host = N bots como jogadores LOCAIS via `AddPlayer_t` + M humanos como clientes remotos; é o padrão
servidor-dedicado, e o cliente humano já sabe "entrar" numa sessão P2P).

## 10. `AddPlayer_t` (bot = jogador local) → exige o CGame inicializado (em progresso)
`AddPlayer_t(CPlayerCharacter&)→CPlayerSource*` (`?AddPlayer_t@CNetworkLibrary@@QAEPAVCPlayerSource@@AAVCPlayerCharacter@@@Z`).
`CPlayerCharacter` ctor (variante Rakion) = `(CTString nome, CTString time)` (`??0CPlayerCharacter@@QAE@ABVCTString@@0@Z`).
Chamado no host APÓS `StartPeerToPeer`. Cadeia de bloqueadores cravada (cada um = um pedaço do "full game init" que o
headless pulava):
1. **`CPlayerSource::Start_t` deref `[0x3636F260]`=NULL** (o global do engine pro objeto **CGame**, vtable[+8]=array de
   players stride 0x378). Fix: `gamemp!GAME_Create()` cria o CGame; seto o global manualmente (o `GAME_Create` sozinho
   não registra). gamemp exporta SÓ `GAME_Create`/`GAME_Destroy` → os métodos do CGame são **virtuais (vtable)**.
2. **Fatal modal "Chunk ID FONN" `Fonts\Boink.fnt`** — o player-init carrega fontes do HUD. Fix: extrair `Fonts.xfs`
   pro `C:\temp\xfs_ext` (o Atalho B serve).
3. **AV em `0x36017E8E`** (`arg0->field4`=NULL): sub-objeto de player-state não-alocado → falta o **`CGame::Initialize`**
   (virtual, carrega `.gms`/settings + aloca slots de player). `GAME_Create` só constrói; falta o init.
**PRÓXIMO:** inicializar o CGame de verdade — achar o slot de vtable do `CGame::Initialize` no `gamemp.dll` (NÃO empacotado,
disassemblável) e chamá-lo com o game-settings do Rakion, OU RE-ar a sequência pós-`GAME_Create` que o `rakion.exe` faz.
Sub-projeto bounded. A sessão+mundo+keep-alive já funcionam (a falha do AddPlayer é capturada por SEH, não derruba o host).

**Endereços (gamemp.dll base 0x10000000):** `GAME_Create`@`0x10018360` = `new(0x25B0)`+ctor+guarda no global gamemp
`0x10036FEC`. `CGame::CGame` ctor@`0x10017150` (seta vtable **`0x100295F8`** em `[CGame+0]`). O engine lê o CGame
no SEU global `0x3636F260` (Rakion-específico; setei manual). vtable[+8] = getter do array de players (stride 0x378).

**`CGame::InitInternal`@`0x10013AE0` ✅ ACHADO E CHAMADO** (não-exportado; achei pelo cluster denso de `DeclareSymbol`
— thunk `0x10026170` carregado em esi @`0x10013C6C`, ~86 `call esi`; prólogo `mov ebx,ecx`=this @`0x10013AE0`).
Chamo via thiscall void(this) após `GAME_Create`+set-global. **Roda OK** (precisa de `Scripts.xfs`+`Controls.xfs`
extraídos pro Atalho B). Erros não-fatais: `sam_str*` não-declarados (startup-script genérico) e "Cannot load game
settings" (`gm_fnSaveFileName` vazio — chamei InitInternal direto, não o `Initialize` virtual que setaria o `.gms`).

**BLOQUEADOR FINAL do AddPlayer (após InitInternal): o APPEARANCE do bot.** O AddPlayer ainda crasha IGUAL em
`0x36017E8E` (`arg0->field4`=NULL) ao criar o `CPlayerEntity` — InitInternal não mudou. Causa provável (aviso do RE
SE1, §6): o `pc_aubAppearance[32]` do `CPlayerCharacter` está ZERADO → o modelo/classe do player não resolve →
sub-objeto NULL. **Rakion usa formato PRÓPRIO de appearance** (modelos `.smc`/`.bmc` em `modelssv\players\<classe>\
<facção>\`, não o `.amc`/`ps_achModelFile` do SeriousSam) — o blob de 32B do SE1 não mapeia 1:1. O servidor OpenRakion
representa o bot por `CharClass` (não o blob cru). **CONFIRMADO + formato:** teste com appearance não-zerado (escrevi "battlebow" @offset 0) → o crash MUDOU para
`0x3601FE50` com `eax=0x6F62656C="lebo"` (= os bytes do appearance offset 4) → a função `0x3601FE40` é um **strlen**
(`mov cl,[eax]`), ou seja o código lê **um `char*` (nome do modelo) embutido no appearance** e faz strlen nele.
⇒ o appearance do Rakion é um **struct com um CTString (model name) por ponteiro**, NÃO bytes opacos (zerado→NULL;
lixo→ponteiro inválido). **Consequência:** um dump CRU de 32B não transfere (o `char*` é por-processo). A forma
correta é a **serializada** (CTString operator`<<`/`>>` escreve/lê o CONTEÚDO, sem ponteiro).
**PRÓXIMO (sub-projeto, precisa do cliente vivo):** capturar a forma SERIALIZADA do `CPlayerCharacter` de um humano
real entrando num stage — hook em `operator<<` (`??6@YAAAVCTStream@@AAV0@AAVCPlayerCharacter@@@Z`) injetado no
`rakion.exe`, OU (mais limpo, quando a integração existir) o humano JOINA o bot-host e o `MSG_REQ_CONNECTPLAYER` chega
ao host com o appearance real → o host desserializa via `operator>>` (reconstrói o CTString com ponteiro válido) e usa
no `CPlayerCharacter` do bot. Aí o `CPlayerEntity` cria e o AddPlayer completa.

## Tabela de endereços (base 0x36000000)
| Símbolo | VA |
|---|---|
| `StartPeerToPeer_t@CNetworkLibrary` | 0x360F4380 |
| `JoinSession_t@CNetworkLibrary` | 0x360F5960 |
| `InitCRCGather@CNetworkLibrary` (PATCH→ret) | 0x360F41E0 |
| `CheckVersion_t@CNetworkLibrary` | 0x360F00D0 |
| `CWorld::Load_t` | 0x360D6D70 |
| `CTFileStream::Open_t` | 0x3603E920 |
| `CTStream::AllocateVirtualMemory` | 0x3603B600 |
| `CTFileStream::FileReadAhead_t` (read lazy por fd `[esi+0x54]`) | 0x3603BB70 |
| `CTStream::PeekID_t` (AV atual) | 0x3603AF40 |
| expect-string helper (AV antigo, lido por ReadFromText_t) | 0x3603C7B0 |
| `CTStream::ExpectID_t` (exportado, ≠ AV antigo) | 0x3603AFE0 |
| `CTStream::GetStreamCRC32_t` (PATCH→ret0) | 0x3603C660 |
| `xfsMan::InitXfsFile` | 0x3603E520 |
| `xFileManager::open` | 0x3601B430 |
| `SE_InitEngine` | 0x36008360 |
| `_pNetwork` (data) | 0x362BA778 |
| world-load array-append (AV-write em buffer reservado, fix=auto-commit) | 0x3601F61E |

### EntitiesMP.dll (ASPack, base FIXA 0x35000000 — no-reloc; carregar só com DEP off)
| Símbolo | VA |
|---|---|
| EntitiesMP ImageBase (handle do `LoadLibrary`) | 0x35000000 |
| EntitiesMP entry-point (stub ASPack; AV aqui = DEP on) | 0x354C1001 |
| export `CWorldBase_DLLClass` (5267 exports restaurados no unpack) | 0x35392160 |
| gamemp.dll ImageBase | 0x10000000 |

### Sessão viva + join (export names da engine.dll)
| Símbolo | VA / export |
|---|---|
| `MainLoop@CNetworkLibrary` (loop por-frame; CRASHA headless) | 0x360F58B0 / `?MainLoop@CNetworkLibrary@@QAEXXZ` |
| `HandleTimerHandlers@CTimer` (pump do host = ServerLoop) | `?HandleTimerHandlers@CTimer@@QAEXXZ` |
| `_pTimer` (global; deref p/ this do HandleTimerHandlers) | `?_pTimer@@3PAVCTimer@@A` |
| global de render nulo headless (deref no MainLoop → AV) | data 0x3636F260 (crash em 0x3610CDE1) |
| `CNetworkSession(const CTString&)` ctor | `??0CNetworkSession@@QAE@ABVCTString@@J@Z` |
| `JoinSession_t(CNetworkSession&, INDEX)` | `?JoinSession_t@CNetworkLibrary@@QAEXABVCNetworkSession@@JVCTFileName@@@Z` |
| `AddPlayer_t(CPlayerCharacter&)→CPlayerSource*` | `?AddPlayer_t@CNetworkLibrary@@...` |

## 11. Fluxo do AddPlayer cravado pela FONTE SE1 (local em `Rakion\Serious-Engine`)
A fonte open-source do SE1 (baixada pelo usuário) mapeou o fluxo EXATO do `AddPlayer` — e refutou a hipótese da
aparência:
- `CPlayerCharacter` (Engine/Entities/PlayerCharacter.h): `pc_aubGUID[16]@0` + `pc_strName@0x10` + `pc_strTeam@0x14`
  + `pc_aubAppearance[32]@0x18`. A aparência é serializada/transmitida **CRUA** (`Write_t`/`operator<<` do
  `CNetworkMessage` fazem `Write(pc_aubAppearance,32)`) — não há sutileza de ponteiro no WIRE.
- `CPlayerSource::Start_t` (Network/PlayerSource.cpp): manda `MSG_REQ_CONNECTPLAYER<<pc` e, sendo server, roda
  `_pNetwork->TimerLoop()` no loop — **o servidor processa a mensagem ali mesmo**.
- Server: `MSG_REQ_CONNECTPLAYER` (Server.cpp:1256) → emite `MSG_SEQ_ADDPLAYER` → `CSessionState` (SessionState.cpp:1325):
  `penNewPlayer = CWorld::CreateEntity_t(pl, "Classes\Player.ecl")` → `AttachEntity` → `en_pcCharacter = pc` →
  `penNewPlayer->Initialize()`.
- `CWorld::CreateEntity_t`→`CreateEntity` (World.cpp:195) → **`pecClass->New()`** instancia o entity. O `New()` chama
  o `dec_New` da CDLLEntityClass = ctor do **CPlayer do entitiesmp** (Rakion, proprietário).

**CRAVADO:** o crash `0x36017E8E` é o helper de engine `0x36017E60` chamado pelo ctor do CPlayer (entitiesmp),
lendo `arg0->field4`=NULL. Registradores `edi=0x3636F75C`, `S[+0]=0x3636F338` (perto do global do CGame `0x3636F260`)
⇒ é um **sub-objeto de game-state do CGame que está NULL**. A aparência (zerado vs lixo) só mudava o crash por
mascaramento — o bloqueador real é o game-state incompleto. **Causa provável: `CGame::InitInternal` NÃO completou** —
rodou o startup-script GENÉRICO (erros `sam_strFirstLevel não declarado` = símbolos do SeriousSam, não do Rakion) e
falhou o "load game settings" (`gm_fnSaveFileName` vazio). **PRÓXIMO:** fazer o `InitInternal` completar de verdade —
descobrir o startup-script/`.gms` REAL do Rakion (o `rakion.exe` passa no `Initialize`) e os símbolos que ele declara,
pra o game-state ficar completo; OU RE do ctor do CPlayer no entitiesmp DESEMPACOTADO (já carregado em 0x35000000;
dump em `C:\temp\entitiesmp_dump.bin`) pra ver qual sub-objeto de CGame ele exige. A fonte SE1 local acelera o RE do
framework (o entitiesmp do Rakion é fork; o esqueleto é o mesmo).

## 12. O MURO do AddPlayer cravado: game-state singletons não-inicializados (precisa do game-mode/match)
SEH enriquecido (dump de `arg0=[ebp+8]`) cravou: o crash `0x36017E8E` lê **`arg0=0x3636F75C`** — um global em
engine.dll **`.bss`** (perto do CGame global `0x3636F260`), **TODO ZERO** (`arg0->field4`=NULL → deref). É um de um
**cluster de game-state globals/singletons** "construct-on-first-use" (ex.: `0x3636F338`: guard `0x3636F38C`, ctor
`0x36198400`, accessor `0x361986E0`). Estão zerados porque o **fluxo headless nunca exercita a init de match/game-mode**
que os constrói. Provas:
- Chamar o accessor `0x361986E0` **constrói** `0x3636F338` (vira `[0]=vtable 0x36235668`) — mas **NÃO ajuda**:
  `0x3636F75C` é separado, continua zero, AddPlayer crasha igual.
- O `StartPeerToPeer` foi chamado com **session-properties (2048B) + spawn-flags ZERADOS**. Esse blob é
  **Rakion-específico** e é o que **configura o game-mode** (Golem War etc.). Sem ele, o game-mode não sobe → os
  singletons de game-state ficam zero → a criação do `CPlayerEntity` (que lê esse estado) deref NULL.

**VEREDITO:** o headless carrega o WORLD (engine-side) ✅, mas criar um PLAYER exige o **runtime de jogo do Rakion**
(game-mode/match + seus game-state singletons), que é montado pela init **lado-game** (entitiesmp/gamemp, proprietária)
guiada pelas **session-properties** da partida. Replicar isso headless = (a) obter um blob de session-properties VÁLIDO
(capturar de uma partida real OU RE do formato Rakion) e passá-lo ao `StartPeerToPeer`, e/ou (b) achar+chamar a função
de **stage/match-init** do game-side que constrói os singletons (como achei o `InitInternal`), e/ou (c) iterar
inicializando cada game-state global. É um sub-projeto **aberto** (interdependências), não "mais um patch". A fonte SE1
local acelera o framework; o miolo (game-mode do Rakion) é proprietário (entitiesmp desempacotado em 0x35000000).

### 12.1 Captura do cliente real + run do host com DIAG (2026-06-28) — branch exato cravado
Leitura READ-ONLY (SeDebugPrivilege) da memória de um stage real (humano GoHeroi + bot OpenRakion no Golem War) +
run do `engine_host.exe host` com diagnóstico fecharam a causa-raiz:

- **`Start_t@CPlayerSource`@`0x36103230` ramifica em `_pNetwork->[0x14]`** (`mov eax,ds:0x362ba778; mov eax,[eax+0x14];
  test;` `je 0x3610371b`). `[0x14]` = discriminador **servidor/cliente** (≈ `ga_IsServer` da fonte SE1, layout
  `CMessageDispatcher`+0 → primeiro membro `BOOL ga_IsServer` em +0x14):
  - **`[0x14]==0` (cliente real, capturado):** caminho `0x3610371b` — referencia o **CGame** (`0x3636F260`), chama
    `CGame->vtable[+8]()` e lê `[ret+0x470c]`. **NÃO copia** o char global → não crasha. A entidade já existe (criada
    pelo servidor); o cliente só a referencia.
  - **`[0x14]==1` (headless = servidor, medido):** cai no caminho que **cria o `CPlayerEntity`** (a pilha do crash tem
    retorno em **entitiesmp.dll** `0x35019412`) e **copia** um `CPlayerCharacter` da região do **singleton de match
    `0x3636F338`** (o copy-helper `0x36017E50` recebe `arg0=0x3636F75C` = `0x3636F40C` _base do char_ `+0x350`).
    `0x3636F40C` está **a +0xD4 dentro** do singleton `0x3636F338` (não é um char isolado) e está **ZERADO** → AV.
- **Refutado que fosse o appearance / o char do bot:** um `CPlayerCharacter` recém-construído tem `field4@+0x354 = 0`
  (sub-objeto `+0x350` nasce com ponteiro nulo) — copiá-lo crasharia igual. E na captura do cliente real a região
  `0x3636F40C..0x3636F75C` está **toda zero também**; o cliente real só não crasha porque pega o caminho `[0x14]==0`.
- **Hack de de-risk refutado:** `memcpy` de um char válido em `0x3636F40C` **corrompe o singleton** (offsets +0xD4..)
  → o crash só anda pra frente (dentro do processamento do singleton). Confirma que `0x3636F40C` é membro do singleton.
- O singleton `0x3636F338` tem vtable `0x36235668` (5 métodos virtuais + dados `"%02X"`; sem RTTI) — objeto pequeno
  tipo stream/feed (combina com a string **"[Attack] GoHeroi"** = feed de combate) **embutido in-place** na `.bss`.
- **Bug corrigido no host:** `pcBuf[256]` mas `CPlayerCharacter` = `0x370`B → overflow de stack (agora `[0x400]`).

**BIFURCAÇÃO ARQUITETURAL (decisão do usuário):** o headless usa a `engine.dll` do **CLIENTE** mas roda o **caminho
SERVIDOR** do AddPlayer, que espera game-state que só a init de match (lado-game, guiada pelas session-properties) OU o
**binário do servidor dedicado `worldserv.exe`** popula. Opções: **(b1)** RE+chamar a match/game-mode-init do game-side
que popula o singleton `0x3636F338` (achar a função como achei `InitInternal`); **(b2)** hospedar o bot com a engine do
**`worldserv.exe`** (binário servidor, caminho servidor é o nativo dele) em vez da `engine.dll` do cliente; **(b3)**
voltar ao caminho de SÍNTESE server-side (o bot já SPAWNA via entity-message tipo 7; o muro é só o **movimento 0x30a**) e
atacar o 0x30a por outro ângulo. (b1/b2 = headless real; b3 = abandona o headless.)

### 12.2 b2 REFUTADO + miolo do crash cravado (2026-06-28)
- **(b2) MORTO:** `worldserv.exe` (servidor dedicado original) **não importa/carrega `engine.dll`/`gamemp`/`entitiesmp`**
  — é socket+MySQL+relay com classes próprias (`CWorld`/`CField`/`UdpSocket@PerfLib`). Não há SE1 server pra "pegar
  emprestado"; a simulação 3D é client-side P2P. Ver [[worldserv-nao-roda-engine-se1]].
- **Módulos desempacotados dumpados** (do próprio processo do host, sem anti-tamper): `C:\temp\ent_unpacked.bin`
  (entitiesmp @0x35000000, 5100032B) e `C:\temp\gamemp_mem.bin` (gamemp @0x10000000, 253952B) — ativos reusáveis de RE
  (entitiesmp é ASPack: só existe desempacotado na memória). O dump fica no DIAG do host (`dumpMod`).
- **Miolo do crash do AddPlayer cravado** (copy-fn = `0x3601A2B0` `operator=`; caller = `0x361972E0`):
  ```
  mov ecx,[0x3636f260]; call [CGame_vt+0x8]   ; controller/player-array do CGame
  movzx ecx,[eax+0x470c]; imul ecx,0x378       ; idx do jogador LOCAL * stride do slot
  lea edx,[arg + ecx + 0x1ac]; call 0x3601A2B0 ; operator=(dst, players[localIdx]+0x1ac)
  ```
  ⇒ o caminho-servidor do AddPlayer **copia o ESTADO DO JOGADOR LOCAL** (`players[localIdx]+0x1ac`). Bloqueio:
  (a) **não há jogador local ainda** (estamos criando o 1º) e (b) o array/game-mode está **zerado** (StartPeerToPeer com
  session-properties ZERADAS). `0x3636F40C`/`0x3636F75C` não são globais soltos — são `players_base+0x1ac+...`.
- **session-properties (o lever):** o blob Rakion-específico (≤2048B, último param do `StartPeerToPeer`) configura o
  game-mode. A captura do `_pNetwork` (`cap_network.bin`) **não** as contém — vivem no `ga_sesSessionState` (objeto
  heap separado, fora dos 0x4000B dumpados). Obter um blob VÁLIDO exige nova captura específica do `CSessionState` do
  cliente OU RE do formato. **b1 segue um sub-projeto ABERTO** (cadeia: local-player-state ← game-mode ← props).

### 12.3 O CGame real É uma classe do RAKION.EXE — b1 precisa da camada do executável (2026-06-28)
A captura do `CGame` real (`cap_cgame.bin`, do ponteiro em `0x3636F260` no cliente vivo) tem **`[0] = vtable
0x004DDC08` — em RAKION.EXE** (não no gamemp `0x100295F8`). Ou seja: o "controller de jogo" que a engine deref em
`CGame->vtable[+8]()` (pra achar o player-array + índice do jogador local) é uma **subclasse do CGame implementada no
rakion.exe** (o executável), que OVERRIDE os métodos virtuais. Prova: `gamemp` `CGame::vtable[+8]` (`0x10011BD0`) é um
**stub `ret` vazio** — o gamemp sozinho NÃO devolve o player-array; quem o faz é o override do rakion.exe.

**Consequência dura pro b1:** o host headless carrega engine.dll+gamemp+entitiesmp, mas a **camada de jogo
(CGame-subclasse + lógica de match) vive no rakion.exe**, que é o EXECUTÁVEL — não dá pra instanciar suas classes peça
a peça num outro processo (rakion.exe tem um entry-point único que roda o jogo inteiro com gráficos). Hospedar o bot
com a engine do cliente headless exigiria **reimplementar a camada rakion.exe** ou **rodar um rakion.exe real** (= 2º
cliente, vetado). ⇒ **b1 é muito mais fundo que "achar +1 init"**; o caminho server-side (b3 — servidor dirige o 0x30a)
é o único que mantém o bot fora de um 2º cliente, SE o cliente aceitar o 0x30a server-relayed (em apuração no binário).

## 13. AddPlayer headless — cascata subida (2026-07-06 noite) → desemboca no APPEARANCE
Com o host RODANDO (build.ps1 + engine_host.exe), subi a cadeia de game-state do AddPlayer degrau a degrau:
1. **localChar global `0x3636F40C` zerado** → AV no copy-helper `0x36017E50`. FIX: `memcpy(pcBuf → 0x3636F40C, 0x370)`.
   O caminho cliente (`pNet->[0x14]=1`) copia o CPlayerCharacter LOCAL desse global.
2. **RNG/hash singleton `0x3636F338`**: guard `0x3636F38C`=01 (accessor `0x361986E0` no-op). FIX: limpar o guard +
   reconstruir → tabelas `[+0x20/+0x24/+0x28]` re-alocadas (construtor `0x36198400`), `[+0x30]`=seed pequeno válido.
3. **Crash SEGUE em `0x3600C52F`** — MAS agora o índice enorme vem do CALLER, não do RNG. Rastro: `0x3600c510`
   (hash) ← `0x3600c4d0` ← **`0x360186d0`**. O caller `0x360186d0` faz `edx=[arg+4]; push edx` → passa `[arg+4]`
   como índice ao hash. **`[arg+4]` é um PONTEIRO** (ex.: 0x0385D2B8), não um índice pequeno → `[tabela+ptr>>3*4]`
   fora dos limites.
   ⇒ É a **serialização do CPlayerCharacter hasheando um campo-ponteiro** = o **APPEARANCE/model como `char*`**
   (RE §10: appearance do Rakion é struct com CTString por ponteiro, não blob opaco). Bate com o "bloqueador final".

**VEREDITO:** a cascata do AddPlayer headless-sozinho termina no APPEARANCE serializado — que precisa da forma
REAL (não um ponteiro por-processo). Resolução limpa = **humano JOINA o bot-host** → `MSG_REQ_CONNECTPLAYER` traz
o appearance serializado real → host desserializa (`operator>>`) → CPlayerCharacter válido → AddPlayer completa.
É a milestone de INTEGRAÇÃO (H3.5), não mais um degrau headless isolado. Alternativa (injeção, vetada): capturar
o `operator<<` do CPlayerCharacter de um humano no rakion.exe.

## 14. Mecanismo da H3.5 — (de)serialização do CPlayerCharacter (o fecho do appearance)
A engine EXPORTA os operadores de stream do CPlayerCharacter — é o que resolve o appearance sem forjar bytes:
- `operator>>(CTStream&, CPlayerCharacter&)` = `??5@YAAAVCTStream@@AAV0@AAVCPlayerCharacter@@@Z` — **DESSERIALIZA**
  (reconstrói os CTString por CONTEÚDO → ponteiros VÁLIDOS por-processo, não o placeholder-sentinela).
- `operator<<(CTStream&, CPlayerCharacter&)` = `??6@...` — serializa.

**Fluxo H3.5 (fecha o AddPlayer):** humano joina o bot-host → `MSG_REQ_CONNECTPLAYER` chega com o
CPlayerCharacter serializado do humano → host aplica `operator>>` p/ desserializar → appearance VÁLIDO →
`AddPlayer` completa p/ esse peer. Para o BOT, o appearance pode ser: (a) clonado do primeiro humano que
entra (desserializa e reusa o blob trocando nome/GUID), ou (b) capturado uma vez e embutido como asset.
⇒ NÃO precisa reverter a construção class→modelo do zero; basta um blob serializado REAL (via operator<<
de um humano) desserializado por operator>>. Esse é o insumo que faltava mapear. Ver §13 p/ a cascata.

## 14. CGame::Initialize (vtable[+0xCC]) — o init de partida REAL (2026-07-07)
Cravado do rakion.bin: após GAME_Create, o exe chama **`CGame->vtable[+0xCC]`** (=gamemp `0x1001f2d0`),
NÃO o InitInternal direto. Essa é a `CGame::Initialize`: chama InitInternal INTERNAMENTE + o resto do
bring-up. thiscall(this, CTFileName* settings, int mode).

Infra que ela exigiu (implementada no engine_host.cpp):
- **Fallback loose-file no Atalho B**: `Initialize` carrega `ControlsSV\Macros.ctl` (e outros) que existem
  LOOSE em rakion-final\ (não no XFS). `FillBufferFromXfs` agora tenta `xfs_ext\<path>` E `<dataRoot>\<path>`.
- **Supressor de MessageBox**: `Initialize` dispara MessageBox modais (headless TRAVA esperando clique — o
  usuário viu um modal de título ilegível). Patch na entrada de user32!MessageBoxA/W → loga+IDOK.

Com isso a `Initialize` roda MUITO fundo: sound devices, IFeel, e **"Initializing TCP/IP... opening as
server... winsock opened ok... port: 25600"**. ⇒ ela sobe o JOGO INTEIRO, incluindo a rede como servidor.
**HANG após "port: 25600"** — bloqueia num ponto de rede (provável espera/loop de servidor, ou master-server).

**Assets XFS ainda não extraídos** (não-fatais, buffer vazio): `DataSetup\UnusableCharName.txt`,
`AbuseString.txt`, `LevelData\LevelList.txt` — só existem no XFS, faltam no xfs_ext.

**FORK de escopo:** chamar a `Initialize` INTEIRA sobe o jogo todo (e pendura na rede). Duas saídas:
(a) domar o bring-up completo (achar/skipar o bloqueio de rede + extrair os DataSetup) — sobe o jogo
headless de verdade; (b) surgical: achar DENTRO da Initialize só a alocação do array de players (o membro
que o getter vtable[+8] devolve) e chamar só isso, pulando sound/input/net. (b) é mais cirúrgico mas exige
RE dos internos da Initialize. Estado: mais fundo que nunca, mas o AddPlayer ainda pende do player-array.

## 15. O hang da CGame::Initialize é DEADLOCK de ambiente (watchdog, 2026-07-07)
Watchdog (suspende a thread principal após 8s + dumpa EIP) cravou: a `Initialize` pendura em
**`eip=0x775A123C` (DLL de SISTEMA)** — uma chamada bloqueante durante a subida da rede ("opening as
server, port 25600"). Não é asset faltando nem crash: é a `Initialize` subindo o JOGO INTEIRO (som +
input + rede + servidor) e **bloqueando num syscall que espera o ambiente completo** (thread de
message-loop / evento que outra thread do jogo sinalizaria) — que o headless não tem.

**Escopo confirmado:** chamar `CGame::Initialize` wholesale = rodar o jogo inteiro headless. É o
núcleo-muro visto do ponto mais fundo. Duas saídas, ambas sub-projeto:
- (a) **Domar o bring-up**: identificar o syscall bloqueante (hook winsock/wait SÓ na janela da
  Initialize) + skipar o master-server/registro + prover as threads que ele espera. Grande.
- (b) **Cirúrgico**: RE dos internos de `0x1001f2d0` (7 calls: 0x1001f230, imports, InitInternal
  0x13ae0, ...) p/ chamar SÓ a alocação do array de players (o membro que o getter vtable[+8]
  devolve), pulando sound/input/net. Mais limpo, exige mapear qual call aloca os players.

Infra pronta p/ retomar: loose-fallback, supressor de MessageBox, watchdog (arma via g_wdArm em volta
da chamada suspeita). Tudo no engine_host.cpp.

## 16. O deadlock é uma CADEIA de modal-loops (peeling, 2026-07-07)
Watchdog + patches do syscall-stub provaram: a `CGame::Initialize` sobe a UI inteira do jogo e pendura
numa CADEIA de message-waits. Peeling com PatchRet0 no win32u/user32:
- `win32u!NtUserWaitMessage` (hang inicial) → patchado (ret 0) → o hang MOVEU p/ `USER32.dll+0x40756`
  (outro modal-loop). Cada patch avança um degrau. ⇒ é o startup-UI completo do jogo, headless.

Infra de bypass pronta (engine_host.cpp): supressor de MessageBox, `PatchRet0` (GetMessage/WaitMessage +
win32u NtUserWaitMessage/NtUserGetMessage/NtUserMsgWait), watchdog com **nome de módulo** (mapa
pré-capturado, sem loader-lock), fallback loose-file.

### Mapa dos 7 calls de CGame::Initialize (0x1001f2d0) — p/ o caminho CIRÚRGICO
1. `0x1001f230` (interno gamemp) — **init principal (som/input/rede/UI); o hang aninha AQUI**.
2. `MessageBoxA` (0x10026908) — erro (suprimido).
3. `exit` (0x100266b8) — condicional (fatal).
4. `CTFileName::operator=` (0x100262f4, ecx=[esi+0x40]).
5. **`InitInternal` (0x10013ae0)** — já chamamos direto, roda SEM hang.
6. `DisableInput@CInput` (0x10026214).
7. `EnableInput@CInput` (0x10026018).

**Veredito:** completar = ou (a) PEELING da cadeia de modais do startup-UI (grinding: cada NtUserWait/
modal → PatchRet0 → próximo; pode ser dezenas), ou (b) CIRÚRGICO: RE de `0x1001f230` p/ isolar SÓ a
alocação do player-array (o membro do CGame que o getter vtable[+8] devolve), pulando som/input/rede/UI.
Ambos são sub-projeto focado. O headless está no ponto mais fundo já alcançado; falta domar o startup-UI.

## 17. Internos do init principal 0x1001f230 — sequência de Read*FromFile (insumo cirúrgico)
Desmontado o call 1 da Initialize (0x1001f230). São ~16 chamadas, quase todas **cargas de dados** (não
UI/rede/alocação-de-players):
`ReadUnusableCharNameFromFile`, `Read_AbuseString_FromFile`, `ReadMacroTextFile`, `ReadLevelDataFromFile`,
`ReadLanguageFile`, `ReadNationCodeFile`, `ReadPreLoadSMCFile` (preload de modelos .smc — candidato ao
init de render/janela), `ReadCreatureListFile`, `ReadSupportNationFile`, **`ReadPlayerDataFromFile`**,
`ReadNpcDataFromFile`, `ReadItemDataFromFile` (+ MessageBoxA/exit de erro entre elas).

**Consequência p/ o cirúrgico:** o array de players NÃO é alocado aqui (são file-reads); nem em
`InitInternal` (chamamos direto, não aloca — getter vtable[+8] segue stub). Falta localizar QUEM aloca o
membro do CGame que o getter devolve — provável dentro do `ReadPlayerDataFromFile` ou de um call pós-reads.
O `ReadPreLoadSMCFile` é o suspeito nº1 do hang de UI (preload de modelos → init de render/janela → modal-loop).

**Resume-point:** (b-cirúrgico) chamar os Read* que populam dados + achar/chamar a alocação do player-array,
PULANDO `ReadPreLoadSMCFile` (render). Toda a infra (loose-fallback, PatchRet0, watchdog c/ módulo, supressor
de modal) está pronta. É o ponto mais fundo já mapeado; o headless roda o init até a subida de render/UI.

## 18. Confirmado: o hang é o ReadPreLoadSMCFile (render/janela) — e é entrelaçado (2026-07-07)
IAT-skip do `ReadPreLoadSMCFile` (no-op) → o **HANG SUMIU** (exit=1 em vez de timeout). ⇒ cravado: o
modal-loop nasce no preload de modelos .smc (init de render → janela). MAS pular inteiro cascateia: o
processo agora **sai (exit 1) na etapa de rede** ("opening as server, port 25600") — globais de render
que a rede/init tocam ficam NULL sem o preload. Sem MessageBox (não é erro modal); é exit()/crash-em-thread.

Arquivos `DataSetup\*.txt` (UnusableCharName, AbuseString, LevelData\LevelList) são **XFS-only** (xFile≠0),
não extraídos p/ xfs_ext nem loose → buffers vazios nas Read*. Extraí-los (de DataSetup.xfs) é pré-requisito.

**Natureza final do muro (cravada):** o init do jogo é um TECIDO entrelaçado render+janela+rede+dados —
remover o render (skip) derruba a rede; manter o render (peeling dos NtUserWait) faz o modal-loop girar.
Nenhum atalho rápido fecha. Completar exige um dos dois SUB-PROJETOS: (a) **ambiente de render/janela fake**
headless (criar uma janela oculta + WndProc + pump próprio p/ o preload completar sem modal); (b)
**cirúrgico profundo**: RE de dentro do ReadPreLoadSMCFile p/ separar carga-de-dados de init-de-render, +
extrair os DataSetup.xfs, + prover os globais de render que a rede referencia. É o núcleo-muro no grão mais fino.

**Estado:** headless roda o init até o preload de render; toda a infra pronta (loose-fallback, PatchRet0
win32u/user32, watchdog c/ módulo, supressor de modal, IAT-skip). Resume: extrair DataSetup.xfs + atacar (a) ou (b).

## 19. Caminho (a) PARCIAL: message-injector passa o modal do render (2026-07-07)
Em vez de bloquear os waits (que só giram), o **message-injector** (thread postando WM_NULL na main +
fila pré-criada) faz o message-loop do preload ITERAR e completar. Resultado: o headless passa o modal do
`ReadPreLoadSMCFile` e **chega à etapa de REDE** ("opening as server, port 25600") — mais fundo que o hang.

MAS: (1) é RACY — às vezes passa (exit=1), às vezes pendura num NOVO wait (`NtUserWaitMessage`) da etapa de
rede/pós-preload (o injetor não alimenta a tempo). (2) O término na rede **não passa por `gamemp!exit`** (hook
não logou) → é `ExitProcess`/crash direto. ⇒ o init tem uma SUCESSÃO de message-loops + um exit/crash de rede.

**Infra nova:** `MsgInjectorThread` (PostThreadMessageW), fila pré-criada na main, hook de `exit` (IAT gamemp)
que loga o caller. Tudo no engine_host.cpp.

**Veredito refinado:** path (a) é viável mas precisa de um injetor ROBUSTO (alimentar TODOS os loops, talvez
por hook de GetMessage que retorna WM_NULL sem bloquear em vez de PostThreadMessage racy) + resolver o
exit/crash da etapa de rede (hookar ExitProcess p/ cravar o caller). Ainda sub-projeto, mas o headless agora
PASSA o render e chega na rede — o ponto mais fundo. Resume: injetor robusto (hook GetMessage->WM_NULL) + ExitProcess hook.

## 20. Pump DETERMINISTICO + veredito empírico da cadeia (2026-07-07)
Trocado o injetor racy por um pump DETERMINISTICO: `GetMessage`/`NtUserGetMessage` hookados p/ devolver
**WM_NULL** (return 1, não WM_QUIT) e `WaitMessage`/`NtUserWaitMessage` → return 1 (há msg). Resultado: o
message-loop do preload itera CONFIÁVEL (não mais racy) e passa o render → chega à rede de forma estável.

Hooks de término cravaram a cadeia, degrau a degrau:
- Etapa de rede: **`ExitProcess(1)` chamado (rets: eng+3AC20)** — a rede headless falha e a engine chama
  ExitProcess (FatalError-like). Hook `ExitProcHook` loga o caller.
- **Ignorar o ExitProcess (return em vez de terminar)** → o processo CONTINUA (o fatal de rede não era
  terminal) e vai mais fundo → **hang em `USER32+0x321A9`** (outro wait, ≠ 0x40756 anterior).

**VEREDITO EMPÍRICO (exaustivo):** o init headless é uma CADEIA PROFUNDA de operações dependentes de
ambiente (modal-loops, waits de USER, fatal-exits de rede). Cada bypass revela o próximo — são dezenas. É
definitivamente o "rodar o jogo inteiro sem tela". Infra pronta e reutilizável: pump determinístico
(GetMessage→WM_NULL), hooks exit/ExitProcess (log + ignore-exit opcional), watchdog c/ módulo, loose-fallback,
supressor de modal, IAT-skip. Resume: continuar peelando (USER32+0x321A9 → identificar o wait → hookar) OU o
cirúrgico (isolar o player-array). Ambos sub-projeto; o headless agora PASSA render+rede — ponto mais fundo.

## 21. DECISIVO: o fatal de rede é REAL — não é "peelar", é PORTAR headless (2026-07-07)
O hang pós-ignore-exit (USER32+0x321A9) está dentro do **`PeekMessageW`** → não é um novo modal a peelar; é
a engine GIRANDO num spin de PeekMessage num estado QUEBRADO. ⇒ o `ExitProcess(1)` da etapa de rede é um
**FatalError GENUÍNO**: a rede headless FALHA de verdade (após bind do port 25600) e a engine aborta
corretamente. Ignorá-lo não avança — só quebra o estado.

**Veredito FINAL (empírico, decisivo):** não é uma cadeia de waits a bypassar um-a-um — cada subsistema
(render, REDE, input) precisa **FUNCIONAR** headless, não ser pulado. Bypassar leva a spin/estado-quebrado.
Logo o HIT×N nativo exige **portar o cliente Rakion pra rodar sem tela** (render nulo + rede-servidor
funcional + input stub), que é o núcleo-muro do projeto — um sub-projeto de porte, não de patches.

**O que o headless HOJE faz (ponto mais fundo, commitado):** carrega o mundo, roda o init do jogo, passa o
render (pump determinístico) e chega até o init de REDE — onde a falha real da rede-servidor headless barra.
Toda a infra (pump, hooks, watchdog, fallbacks) fica reutilizável. Resume do sub-projeto: fazer o
`StartPeerToPeer`/server-open da engine ter sucesso headless (RE do que a rede-init exige sem render) — é o
próximo alvo REAL, não mais peeling.
