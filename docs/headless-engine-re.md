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

## 22. VIRADA: o muro era o modo SERVIDOR — o JOINER contorna o fatal (2026-07-07)
§21 dava o veredito como "portar headless". Estava certo **para o modo HOST** (o peer sobe o jogo COMO
servidor: `CGame::Initialize` → "opening as server, port 25600" → `ExitProcess(1)`). Erro de arquitetura:
para pôr um 2º combatente no cliente humano, o headless **não precisa ser servidor** — precisa ser
**JOINER** (o 2º cliente que ENTRA na sessão que o humano hospeda). É o split master/joiner do stage real
2-clientes (`room-state 0x37` master@+2; sim-3D é P2P client-side, `worldserv` não roda engine).

**RE do binário CERTO (rakion-final, via capstone — corrige offsets cross-build):**
- CGame vtable **@0x100295f8** (do ctor 0x10017150, `mov [esi],0x100295f8`). `Initialize`=vtable[+0xCC]=
  **0x1001f2d0** (confirmado); é um WRAPPER fino: `call 0x1001f230` (init grande: Read* + render + rede) →
  MessageBoxA/exit de erro → `operator=` [esi+0x40] → `InitInternal` **0x10013ae0** → `ret 8`.
- vtable[+8] (suposto "getter do player-array" da RE antiga) = **0x10011bd0 = `ret` PURO (stub)** em
  rakion-final. A premissa "vtable+8 devolve o array" veio do **rakion-new** (build diferente) — não vale no
  binário real. Por isso toda a cascata headless-HOST batia em lixo/AV: **offsets de outro build**.
- `AddPlayer_t` real = engine.dll **0x360F3EB0**. Lê estado PROFUNDO do CGame:
  `CGame[0x3636F260]->vtable[+8]()` → byte `[+0x470c]`, slot `[edi+ecx*4+0x4854]`, `[0x362ba778]+0x24 +0x2946`.
  ⇒ o AddPlayer depende de um CGame **inteiro** (o que a `Initialize` monta) — NÃO de uma alocação isolável.
  No JOINER esse estado profundo **vem do host pela sessão** (o join streama world+game-mode+players), não é
  alocado localmente. É por isso que o caminho joiner é viável e o "cirúrgico local" (§16b) não era.

**Correção implementada (engine_host.cpp):** o bloco `CGame::Initialize` virou **host-only**. Em modo `join`
o peer faz só `GAME_Create` + `InitInternal` (0x13ae0, roda limpo, sem hang/fatal) → `JoinSession_t`
(0x360F5960, conecta pra FORA) → `AddPlayer_t` APÓS o join (com o game-state já vindo do host).

**PROVA empírica (smoke-test `engine_host … join Rakion 127.0.0.1 …`):**
```
[c++] JOIN: InitInternal(@10013AE0) direto — pula o server-open da Initialize (§22)
[c++] InitInternal OK (joiner)
[c++] JoinSession_t(host="127.0.0.1", world="…ko2.wld") em SEH-probe…
[commit] 0903xxxx (acc=write) commitada, re-exec   ← aloca buffers da sessão
<segfault>                                          ← conectando a host AUSENTE (127.0.0.1 vazio)
```
**ZERO "opening as server, port 25600". ZERO ExitProcess(1) fatal.** O peer joiner passa o init, entra no
`JoinSession`, aloca os buffers da sessão e só cai por **não haver host** em 127.0.0.1 — falha esperada de
"ninguém pra entrar", não o muro de rede de §21. **O muro do §21 era do modo servidor; o joiner o contorna.**

**Rungs restantes (bounded, arquitetura correta):**
1. **Servidor broka o endpoint do humano + lança o engine_host** em `join` no início do stage. O world
   server JÁ conhece o endpoint UDP-gameplay do humano (relaya 0x30a pra ele) — é o alvo do JoinSession.
   Validar se a porta de sessão-listen do master == porta gameplay.
2. **Teste in-game:** humano hospeda o stage (master), engine_host faz join → `AddPlayer("Bot1")` completa
   com o state do host? O cliente humano cria o combatente real → HIT×N + movimento/anim/colisão nativos.
3. **Ponte de IA (H5):** dirigir o bot alimentando movimento/ataque pela sessão do engine_host (tráfego do
   peer joiner), não mais 0x30a server-relay do fantasma.

**Estado:** o muro do §21 (server-open do modo servidor) está **contornado**. Rungs restantes abaixo.

## 22.1 Teste 2-processos headless (hostmin ↔ join) — o muro REAL localizado (2026-07-07)
Para provar o joiner SEM depender do cliente humano, adicionei `hostmin` (host mínimo: `StartPeerToPeer` +
LISTEN, SEM `CGame::Initialize`) e testei `hostmin` ↔ `join` headless-vs-headless. Resultados:

- **`hostmin` FUNCIONA headless:** `InitInternal` + `StartPeerToPeer_t` **abriu o LISTEN e serviu 45s** —
  SEM `CGame::Initialize`, SEM render, SEM o fatal do §21. ⇒ **REFINA o §21**: a `CGame::Initialize` (com o
  "opening as server") NUNCA foi necessária pro host ESCUTAR; o world-load + listen server-side rodam limpos
  headless. O muro do host era um beco (a `Initialize` faz render+rede que o listen não precisa).
- **`join` CRASHA no world-load do CLIENTE (não no connect):** o segfault é **idêntico com ou sem host** →
  não chega ao connect; morre no setup LOCAL do `JoinSession`. `engine_host.RPT`: `C0000005` @ `009AE60D`
  (região anônima). `engine_opens.log` (últimos opens antes do crash): cascata de **assets de RENDER** —
  `.smc`/`.bm`/`.tex`/ParticleEmitter (`green_build01`, `MessageManager.ecl`, `pwParticleEmitter`). ⇒ o
  JOINER, por ser **cliente**, carrega os MODELOS/TEXTURAS do mundo e derefa um device de render NULL headless.
  É o **muro de render** (§18/§20), agora atingido pela via do joiner (o host `hostmin` NÃO o toca — carga
  server-side sem modelos).

**Veredito refinado (o muro real, localizado):** o server-open (§21) era red-herring — o host-listen headless
FUNCIONA (`hostmin`). Os dois núcleos que sobram são o **mesmo tecido render+game-state** de sempre:
- (a) **joiner headless** precisa de render-null pra carregar os modelos-cliente do mundo (§20 peeling, ou
  criar um device de render nulo), OU
- (b) **host headless** (`hostmin`, que JÁ escuta) + o HUMANO como JOINER (o humano TEM render → sem crash de
  modelo do lado dele; traz o appearance no join, §14 H3.5) — falta só o **game-state pro `AddPlayer(bot)`**
  no host (o estado profundo que o `AddPlayer_t` lê: `CGame vtable+8→+0x470c/+0x4854`), que a `CGame::Initialize`
  monta mas entrelaça com render/rede. Caminho (b) é o mais promissor: o lado que precisa de render é o HUMANO
  (que o tem), e o headless fica só como host+bot.

**Rungs concretos (caminho b, o mais viável):**
1. `hostmin` já escuta headless ✅. Falta: montar SÓ o game-state que o `AddPlayer_t` lê, sem a
   `CGame::Initialize` inteira (isolar dos internos de `0x1f230`/`InitInternal` o que popula
   `CGame vtable+8→+0x470c`). É a RE cirúrgica agora com alvo EXATO (o `AddPlayer_t` @0x360F3EB0 diz os campos).
2. Servidor faz o cliente humano JOINAR o `hostmin` (brokering: apontar o P2P do humano ao endpoint do host),
   em vez do humano hospedar. Traz o appearance (§14 H3.5) → `AddPlayer` do humano completa.
3. `AddPlayer(bot)` no host → propaga ao humano → combatente real → HIT×N. Ponte de IA (H5) dirige o bot.

**Infra desta sessão:** modo `join` (host-only `CGame::Initialize`), modo `hostmin` (listen headless provado),
harness `join-bot-test.ps1`, RE fresca do binário CERTO (vtable/AddPlayer_t/offsets corrigidos).

## 22.2 `AddPlayer_t` real cravado + o muro FINAL = appearance do jogador-local (2026-07-07)
Com `hostmin` (estado LIMPO, sem a `CGame::Initialize` fatalada), testei `AddPlayer(bot)` e cravei o
`AddPlayer_t` REAL do binário certo (a análise antiga do `0x36103740`/`vtable+8` era de um FRAGMENTO errado):

**`AddPlayer_t`@0x360F3EB0 (entrada real):**
- O array de players está em **`pNet+0x28` (count) / `pNet+0x2c` (base), stride `0x370`** — no `CNetworkLibrary`
  (o `SE_InitEngine` monta), **NÃO** no CGame. Headless: `count=4, base=válido` ✅ (o array EXISTE).
- Varre o array achando slot vazio (`[slot+4]==0`), `imul idx,0x370; lea ecx,[base+off]`, e chama o
  **build-do-slot `0x36103230`(ecx=&players[slot], ebp=CPlayerCharacter do bot)**; retorna `&players[slot]`.

**O crash (cravado, estado limpo):** `0x36103230` → helper de cópia **`0x36017E50`**, que faz:
```
mov ecx,[ebp+8]     ; arg0 = 0x3636F75C  (o CHAR do JOGADOR-LOCAL, global)
mov ecx,[ecx+4]     ; ecx = [0x3636F760] = appearance-subobj  (NULL headless)
mov edx,[ecx]       ; deref -> AV em 0x36017E8E
```
⇒ o `AddPlayer` **copia o char do jogador-LOCAL** (global `0x3636F75C`) e derefa seu sub-objeto de
**appearance** em `[+4]`. Headless ninguém conectou → `[+4]=NULL` → AV.

**Fix ingênuo REFUTADO (empírico):** construir um `CPlayerCharacter(name,team)` NO global `0x3636F75C` deixa
`[+4]` com **LIXO** (`0x44B95D81`, não um ponteiro) → a cópia derefa o lixo e crasha IGUAL. ⇒ o appearance
**não** é criado pelo ctor básico; é um sub-objeto serializado que **só nasce de um cliente real**
(`MSG_REQ_CONNECTPLAYER` traz o blob, §14 H3.5).

**VEREDITO (o muro final, no grão mais fino):** o headless-host (`hostmin`) sobe, escuta, tem o array de
players — mas `AddPlayer(bot)` precisa do **appearance do jogador-local**, que não existe sem um cliente real.
Duas saídas concretas (ambas viáveis, não "portar o cliente"):
- **(A) Humano joina o `hostmin`** → o connect dele popula o char-local + appearance no global → `AddPlayer`
  (dele E do bot clonando o appearance dele) fecha. É o caminho (b)/§14 H3.5, agora com o muro exato na mão.
- **(B) Capturar um blob de appearance real** (de um `MSG_REQ_CONNECTPLAYER` de partida real) e instalá-lo no
  global `0x3636F75C[+4]` antes do `AddPlayer` → headless-sozinho fecha. RE do formato do sub-objeto pendente.

**Próximo passo REAL:** (A) — subir `hostmin` + fazer o cliente humano joinar (brokering do endpoint), e ver
o `AddPlayer` fechar com o appearance vindo do humano. É a validação in-game do caminho, com o muro reduzido a
UM ponto conhecido (`0x3636F760` = appearance-subobj do char-local).

## 22.3 CONVERGÊNCIA — dois REs independentes fecham o insumo (2026-07-07)
O decode do connect de sessão (`rakion-work/ghidra-proj/tagv_connect_decode.out.txt`, RE anterior sobre
`ConnectRemoteSessionState@0x36105f30` + captura de 6275 frames) chega ao MESMO veredito desta sessão, por
caminho independente — o insumo agora está COMPLETO e consistente:
- **Forjar o connect server-side = INVIÁVEL.** O TAGV **nunca trafega no fio** (0 hits em 6275 UDP+TCP); o
  connect é um **stream reliable dinâmico** (janela deslizante, ACK por offset, ~315 frames/~2s). O **0x0304
  (connect) e o 0x30a (gameplay) compartilham o MESMO seq de stream** (transição seq 0x26→0x27 no gate). O gate
  do 0x30a abre com a **conclusão do LOAD**, não por frame-gatilho. Provado in-game: o eco do controle
  0x0304/0x0305 NÃO abre o gate (0× 0x30a do host). ⇒ casa 1:1 com a minha RE do `AddPlayer` (§22.2): o estado
  vem de um cliente real, não de bytes sintéticos.
- **Recomendação daquele RE (verbatim):** *"rodar a engine como 2º peer em loopback... é a única via realista
  p/ o bot andar sem reimplementar a netcode da Serious Engine."* ⇒ **é exatamente o `hostmin` desta sessão**, que
  PROVEI rodar (StartPeerToPeer + listen headless). O artefato que aquele RE apontou como "a alternativa" agora
  EXISTE e roda.

**Insumo consolidado (o que a meta pedia — "para termos insumos"):**
1. Síntese server-side do combatente/HIT×N = **inviável** (2 REs convergem). Não perseguir.
2. Peer de engine headless (2º cliente real em loopback) = **a única via** — e **roda** (`hostmin`).
3. Muro final = **um ponto**: o `AddPlayer`/stream precisa do estado-de-cliente que só o connect real traz
   (appearance `0x3636F760`; stream 0x0304→0x30a contíguo).

**Integração p/ FECHAR (o "método p/ fechar" daquele RE, agora concreto):** redirecionar o tráfego de peer do
bot (hoje o relay do servidor em `BotSessionConnect`/`UdpGameplay`) para o **endpoint UDP do `hostmin`** — o
cliente humano passa a fazer o connect-stream contra a **engine real** do `hostmin` (não contra o eco de
controle do servidor). Sinal de sucesso (daquele RE §7): o host **emitir 0x30a** (RX `0a03`) = gate aberto =
bot combatente. Risco baixo: hoje o gate já não abre (bot é fantasma), então redirecionar não piora — só pode
ABRIR. Lifecycle: servidor sobe o `hostmin` no início do stage + descobre a porta + redireciona o slot do bot.

## 22.4 Muro de render CAIU (client=0) + a verdade final do appearance (2026-07-07)
O muro de render do joiner (§22.1, que eu temia ser "peeling de dezenas de derefs") é **UM FLAG**: SE1 tem
modo **dedicated** (sem render device). O `PrepareForUse(useNet=1, client)` — `client=1` (join) cria objetos de
render → crash ao carregar modelos; `client=0` (host/`hostmin`) não. **Forçando `join` com `client=0`
(dedicated):** `JoinSession_t` **RETORNOU OK e carregou o mundo — ZERO crash de render**. O muro de render caiu.
(engine_host.cpp: `join ... ded` = client=0.)

**MAS o teste 2-processos (hostmin `listen` + join `ded`) cravou os limites:**
- O host A **NÃO reagiu** ao joiner B (nenhum log de connect); B "RETORNOU OK" mas **carregou o mundo LOCAL**,
  não sincronizou com A. ⇒ `JoinSession` dedicated headless faz world-load local, mas o **connect P2P real com
  o host não visivelmente completa** (porta/handshake — ou o dedicated não engaja o connect como um cliente).
- B crashou no **mesmo appearance wall** (`0x36017E8E`), com ou sem host. ⇒ **confirmado:** o appearance que
  falta é do **jogador-LOCAL do joiner = o próprio bot**, NÃO um dado que vem do host. O bot, como player local
  (seja no host ou no joiner), não tem appearance real — e appearance real só existe de uma **seleção de
  personagem de um cliente real**.

**VERDADE FINAL (o irredutível, cravado por todos os ângulos):** o combatente/HIT×N exige um player com
**appearance real**; a única fonte é um **cliente real** (a seleção de char do humano). Um bot server-side não
tem isso. ⇒ o funcional exige o **humano numa sessão com o `hostmin`** e o bot **clonando o appearance do
humano** (que chega quando o humano conecta). Não há caminho 100% headless — provado dos dois lados (host e
joiner, ambos batem no appearance do player-local). O render caiu; o gate do appearance é **in-game por
natureza**. Ganho da sessão: render-null resolvido (joiner carrega mundo headless), muro reduzido ao appearance
(1 ponto, `0x3636F760`), e a fonte dele cravada (cliente real → clonar).

## 22.2b Fecho — os DOIS ramos do AddPlayer exigem estado de cliente real (path B não é atalho)
O build-do-slot
`0x36103230` bifurca em `[0x362ba778]+0x14`:
- **`==0` (LOCAL, `0x3610371b`):** usa `[0x3636f260]`(CGame)→`vtable[+8]`→`[eax+0x470c]` + `[0x362ba778]+0x24`
  → precisa do **player-manager do CGame** (a init completa; `vtable+8` é o `ret`-stub sem o estado).
- **`!=0` (CLIENT):** copia o **appearance do jogador-local** (`0x3636F760`) → precisa de um cliente real.
⇒ **irredutível:** um bot hospedado no servidor não tem estado-de-cliente; **um cliente REAL tem de provê-lo**.
Logo **(A) é O caminho** (não uma entre duas opções): `hostmin` hospeda, o HUMANO joina e traz o estado, o
`AddPlayer` fecha, o bot clona o appearance do humano. Instalar um blob isolado (B) não basta — os dois ramos
querem mais que um campo. Sub-projeto restante: brokering (servidor manda o humano joinar o `hostmin`) +
validação in-game + clone do appearance p/ o bot + ponte de IA (H5). O muro está **cravado e localizado**, não
mais difuso; o host headless **roda**; falta a integração do join do humano.

## 22.6 REVIRAVOLTA — o headless SE1 é o PROTOCOLO ERRADO (3 experimentos, 2026-07-07 noite)
Retomei o peer headless e, com a **ground-truth de 2 humanos REAIS** (`docs/p2p-handshake-groundtruth.txt`)
na mão, rodei 3 experimentos que juntos **aposentam** a via headless-SE1 — não por muro de appearance, mas por
**mismatch de protocolo**:

**A verdade do fio (groundtruth l.1-70) — a camada P2P do stage NÃO é sessão SE1:**
- `0x0201 CONNECT` vai do cliente ao **SERVIDOR** (portas 40708/40709 = world/broker), NÃO a outro cliente
  (l.1-10). É o registro de endpoint UDP↔servidor — o que o **nosso** world server JÁ faz (o "0x319
  endpoint-register"). O servidor ECOA 12B de volta (l.3-4).
- O canal cliente↔cliente (2301↔2302) é **SÓ** `0x0304` push/`0x0305` ack (12/13B, l.12-55) + `0x030a`/`0x030f`
  (l.56+). **ZERO `0x0201` entre clientes, ZERO TAGV, ZERO StartPeerToPeer/JoinSession no fio.** A "sessão" P2P
  do stage é o lockstep minúsculo — que o `BotLockstep`/`BotManager.Peer` **já sintetiza**.

**Experimento 1 — joiner dedicated (`join … ded`) NÃO conecta pra fora:** `JoinSession RETORNOU OK`, mas um
sniffer nas portas 2301/25600 capturou **0 datagramas** do joiner. Ele carrega o mundo LOCAL e morre no
appearance wall — **nunca fala com um host** (confirma §22.4: dedicated = world-load local sem connect).

**Experimento 2 e 3 — `hostmin` (StartPeerToPeer) IGNORA o dialeto do stage:** injetei no listen do `hostmin` os
frames golden — primeiro só os `0x0304` opens/pushes (exp.2), depois seguindo a sequência REAL (`0x0201 CONNECT`
→ eco → opens, exp.3). **O `hostmin` não respondeu a NADA** (0 respostas ao CONNECT em 3s, 0 aos pushes). ⇒ o
listen do SE1 `StartPeerToPeer` fala o **protocolo de sessão da SE1**, que **não é** o `0x0201`+`0x0304` do
Rakion in-stage. Por isso "sessao NAO forma" (commit 9833bca) — o cliente humano nunca completaria o handshake
com o `hostmin`, pois o stage não usa sessão SE1.

**VEREDITO (o headless-SE1 é o artefato ERRADO):** rodar a engine SE1 como 2º peer pressupõe que o stage P2P =
sessão SE1. A ground-truth PROVA que não é — o stage P2P é o lockstep custom `0x0304` que **já emitimos**. O
combatente real (HIT×N/kill/colisão) NÃO nasce de uma sessão SE1; nasce do cliente humano receber a **mensagem
de jogo que dispara `AddRemotePlayer`** (o "CPlayer EMPACOTADO" de `hitxn-muro-estado-combatente`:
`AddRemotePlayer@engine 0x3610e2b0`, **sem caller na engine** ⇒ chamado do **gamemp.dll** em resposta a uma
game-message). Essa mensagem viaja pelo protocolo de jogo que **nós controlamos** — não precisa de headless.

**Reorientação (o insumo que sobra, agora com alvo certo):** parar de perseguir a sessão SE1 headless. A frente
real é **achar, no gamemp.dll, o dispatch de game-message que chama `AddRemotePlayer`** e sintetizá-lo
server-side (como já fazemos com 0x38/0x4b/0x0304). É RE de gamemp (base 0x10000000) sobre o call-site do import
`AddRemotePlayer` da engine — o "packed CPlayer" que seta team/alive/template/HP na entidade remota. Sem
processo headless, sem appearance-wall (o appearance vem no blob da própria game-message, como no caso 2-humanos
onde o char do peer chega pelo world TCP + 0x38, não por sessão SE1).

## 22.7 `AddRemotePlayer` cravado + a fonte SE1 nomeia o trigger (2026-07-07 noite, cont.)
Segui a frente nova com capstone/pefile no binário CERTO (rakion-final). Achados concretos:

**A fonte SE1 open-source nomeia o mecanismo (`Sources/Engine/Network/SessionState.cpp:1317`):** o cliente cria
a entidade de um jogador remoto ao processar **`MSG_SEQ_ADDPLAYER`** no `CSessionState::ProcessGameStreamBlock`
— o gamestream CONFIÁVEL, ordenado, que o HOST da sessão manda por tick. O bloco carrega
`[INDEX iNewPlayer][CPlayerCharacter pcCharacter]`; o handler faz `CreateEntity_t("Classes\\Player.ecl")` +
`AttachEntity` + `en_pcCharacter = pcCharacter` + `Initialize()`. **É a criação do combatente real** (o
`CPlayerCharacter` traz nome/appearance/template). Intercalado com `MSG_SEQ_ALLACTIONS` (o tick de ações).

**`AddRemotePlayer` da engine.dll = o handler do Rakion desse mecanismo. Assinatura e miolo CRAVADOS
(`?AddRemotePlayer@CSessionState@@QAEXEGPAD@Z` @0x3610e2b0):**
- `AddRemotePlayer(uchar seat, ushort blobLen, char* blob)` — thiscall (ecx=CSessionState).
- `[0x3636f260]`(CGame)`->vtable[+8]->[+0x4854 + seat*4]`: se já há entidade no seat → pula (idempotente).
- Vazio: `call 0x3610b6d0` (aloca), e se `blobLen>0` monta CTString do **blob** (`0x36100cd0/d50`) → `call
  0x361095b0`(pega o CPlayerSource) → `call [vt+0x118]` (seta o nome/appearance do blob). Depois lê `[+0x470c]`
  do player-manager do CGame e `call [vt+0x114]`/`[vt+0x11c]` (mais setters). ⇒ **o appearance vem do BLOB do
  argumento**, não de sessão SE1. E lê o player-manager do CGame — que **no cliente humano REAL está
  inicializado** (o muro de §22 era EXCLUSIVO do nosso headless sem CGame; no cliente vivo não existe).

**Quem CHAMA (o trigger) está no rakion.exe PACKED — RE estática esbarra no packer:**
- `AddRemotePlayer` tem **0 callers diretos (E8)** e **0 referências de ponteiro** em engine/entitiesmp/gamemp/
  todos os módulos (xref de `b0 e2 10 36` = vazio). ⇒ a claim antiga "sem caller" estava certa PARA os módulos,
  mas o caller existe: **`rakion.exe`/`rakion.bin` IMPORTAM o símbolo** (IAT slot **0x004d01f4**).
- Mas o `.text` do rakion.exe tem **0 referências** ao slot 0x4d01f4 → o executável do cliente é **packed/
  anti-tamper** (o código que referencia a IAT só existe descomprimido em runtime). Estática morre aqui.

**VEREDITO desta frente (mecanismo 100% claro, trigger atrás do packer):** o combatente real nasce de
`AddRemotePlayer(seat, len, blob)` chamado pelo dispatch de stage do rakion.exe quando chega a mensagem de
"novo player" (equivalente Rakion do `MSG_SEQ_ADDPLAYER`). O blob = nome/appearance. NÃO precisa de headless,
NÃO precisa de sessão SE1, NÃO tem appearance-wall no cliente vivo. Falta UMA coisa: **qual mensagem/bytes o
rakion.exe traduz nessa chamada** — e isso só sai por **RE ao vivo** (hook do IAT slot 0x4d01f4 no cliente,
via DLL de diagnóstico do launcher — dev-only — com um 2º player REAL entrando p/ capturar `seat/len/blob` e a
origem no fio). É o método `diagnostico-runtime-quebra-loop-de-RE` + `rakion-final-binario-diferente-re-ao-vivo`.
Artefatos estáticos: `scratchpad/{find_addremote,callers,xref_ptr,disasm_fn,imports_of,ref_imm}.py`.

## 22.8 CAPTURA VIVA do create-combatente + formato da mensagem CRAVADO (2026-07-07 madrugada) ✅
Injeção de DLL FALHOU (anti-tamper bloqueia LoadLibrary: `hmod=0`, DLL não mapeia), mas o **hook EXTERNO por
code-cave** (só `VirtualProtectEx`/`WriteProcessMemory`, as primitivas dos patches de janela) FUNCIONOU. Tool:
`client/RakionDiag/capture_addremote.exe` (hooka em `0x3610e2b6`, logo após o prólogo de 6B — coexiste com um
hook de entrada de outra DLL, ex.: sessprobe). Com **2 clientes REAIS no mesmo stage Golem War**, capturado:

**AddRemotePlayer(seat=10, blobLen=67, blob) — caller = engine.dll `0x36193dcd` (NÃO o rakion.exe packed!).**
O create é chamado de DENTRO da engine.dll — a claim "trigger atrás do packer" de §22.7 estava ERRADA; o
trigger é um handler da própria engine.

**Handler CRAVADO — `FUN_36193d70` (a "create remote player from message"):**
```
0x36193d70: push ebp; mov ebp,esp; and esp,-8; sub esp,0x410
0x36193d81: mov edx,[ebp+8]        ; edx = ARG1 = ponteiro da MENSAGEM
0x36193d8b: mov al,[edx]           ; byte[0] = SEAT
0x36193d91: mov ax,[edx+1]         ; word[1..2] = BLOBLEN (u16)
0x36193d9f: lea esi,[edx+3]        ; &blob = message+3
0x36193dab: rep movsd/movsb        ; copia blobLen bytes p/ buffer local
0x36193db4: mov ecx,[0x3636d240]   ; objeto (CSessionState) ; edx=vtable
0x36193dc0..c6: push blob; push blobLen; push seat
0x36193dc7: call [edx+0x254]       ; == AddRemotePlayer (chamada VIRTUAL vtable+0x254 -> por isso 0 callers E8)
```
⇒ **FORMATO DA MENSAGEM DE CREATE-COMBATENTE: `[seat:u8][blobLen:u16 LE][blob (CPlayerCharacter, blobLen bytes)]`.**
O `blob` traz appearance/stats/template — e o CGame do cliente vivo já está init (sem appearance-wall, §22.2 era
só do headless). `FUN_36193d70` sem caller direto/ponteiro estático (dispatch runtime ou no packer) — mas
IRRELEVANTE: temos o formato + um blob real.

**BLOB REAL capturado (seat 10, 67=0x43 bytes) — golden source p/ sintetizar o do bot:**
```
08 00 00 02 43 00 00 02 43 00 00 00 00 00 00 00
00 1f 00 00 00 01 00 00 00 00 00 00 02 43 00 00
02 43 48 e1 7a 3f 3c 00 00 00 50 00 00 00 78 00
00 00 64 00 00 00 04 00 00 00 00 00 00 00 00 00
00 00 00
```
Pistas: `0x0243`(=579) repetido (class/model id?); `0x3f7ae148`=float 0.98 @+0x22; ints 60/80/120/100/4 @+0x26..
(stats). Decode fino pendente, mas um exemplo real basta p/ replay+adaptar (seat/nome).

**O QUE FALTA (único item p/ HIT×N/kill/colisão nativos): o FRAMING NA REDE.** A `[seat][blobLen][blob]` chega
ao `FUN_36193d70` pelo sistema de mensagem confiável (stream `0x0304` P2P entre peers). Preciso do **wrap exato
no fio** (header do bloco reliable que carrega essa msg) p/ o servidor SINTETIZAR e entregar ao cliente do humano
como se viesse do peer do bot. Como o add-player humano↔humano corre P2P DIRETO (não pelo nosso relay), o próximo
passo é **capturar o UDP no join de 2 clientes** (sniffer no loopback, achar o frame ~70B que carrega o blob
`08 00 00 02 43...`) OU hookar o dispatcher (caller de `FUN_36193d70`) ao vivo p/ ler a msg reliable crua.
REGRA: a entrega final é **server-side** (síntese da msg), injeção só serviu de RE (proibido injetar p/
funcionalidade — [[sem-ddl-injetada-tudo-server-side]]). Tool de captura: `client/RakionDiag/capture_addremote.exe`;
log `C:\temp\addremote_capture.log`.

## 22.9 O bot É criado nativo — mas falta a ATIVAÇÃO DE COMBATE (peer vivo), não o create (2026-07-08)
Teste in-game com o hook + `/addbot`: **o 0x4B do bot DISPARA o `AddRemotePlayer`** (capturado: seat=10,
blobLen=67, mesmo caller `0x36193dcd`; blob do bot estruturalmente idêntico ao humano — lead 08, animId 31,
flag 1, facing 0.98, só posição/stats diferem). ⇒ **o muro do "create" CAIU**: o bot é instanciado como
entidade remota nativa (visível, anda pelo 0x30a relayado). O "fantasma" histórico era do caminho 0x307/type-7,
NÃO deste 0x4B.

**MAS o bot não é atingível** (sem HIT×N, sem animação de dano). Diagnóstico decisivo in-game:
- **2 humanos SE ACERTAM** (HIT×N aparece entre eles) no nosso servidor. Combate funciona com peer real.
- **`0x4C` NÃO é a ativação:** o servidor DROPA o `0x4C` do humano ("NAO-PORTADO 4C em campo") — o humano A
  nunca recebe o `0x4C` do humano B — e mesmo assim os dois se acertam. Sintetizar 0x4C pro bot não resolve.
- Bot e humano recebem o MESMO: `0x4B` (cria) + `0x30a` relayado (move). Iguais. ⇒ a diferença é só o
  **registro de peer de sessão VIVO** (o handshake reliable `0x0304`): o humano completa (ackeia certo, vira peer
  combatível); o `0x0304` sintetizado do bot (`BotLockstep`) aparentemente NÃO fecha esse registro no cliente.
  Casa com [[lockstep-p2p-dialeto-real]] ("validação HIT×N pendente") e [[hitxn-muro-estado-combatente]]
  ("estado de COMBATENTE, não em bytes").

**PRÓXIMO PASSO cirúrgico (task #21):** pôr 1 humano + 1 humano REAL + o bot no MESMO stage; hookar e **comparar
em memória a entidade-humano (combatível) vs a entidade-bot (fantasma)** — ambas em CGame player-manager
`[+0x4854+seat*4]`. O campo que difere (alive/team/HP/`CPlayerSource`-connected) é o alvo EXATO: ou o servidor
seta esse estado via uma msg que o bot ainda não manda, ou o `0x0304` do bot precisa fechar o registro de peer.
Tool: `capture_addremote.exe` (estender p/ dumpar a entidade dos dois seats).

## 22.10 Correção da hipótese do gametick: o stage usa o P2P UDP do Rakion (2026-07-10)
A hipótese de que o `0x4B` fazia o cliente esperar `MSG_SEQ_ALLACTIONS` foi descartada ao voltar para a captura
completa de dois humanos: não há esse bloco no fio do stage. O combate observado usa a família P2P UDP
`0x830c → 0x0311 → 0x8315`, além de `0x030a/0x030f` e do canal `0x0304/0x0305`.

A captura também mostrou que todo `0x83xx` entregue recebe um ACK `0x4000`, com a sequência confirmada no
offset 7. O World consumia esse ACK e o `0x8315`; com bot presente, o `0x8312` ficava pendente e o combate entre
humanos não ativava corretamente. O hub agora registra o remetente de cada reliable por destinatário e devolve
o ACK ao peer original. ACK de reliable emitido pelo bot é consumido porque não existe socket cliente do bot.

Arquitetura adotada: o bot continua sendo um ator autoritativo do servidor, ocupa roster/seat e recebe `0x4B`
em todos os clientes. Presença e movimento usam o P2P capturado. Contra type-7, o cliente não emite o `0x8315`;
o World combina o `0x311` de intenção com o vetor `aimX/aimZ` do próximo `0x30a` e testa um segmento contra os
corpos inimigos. Dano e `0x315` visual só nascem se esse segmento atravessar primeiro o bot. Combate
humano→humano permanece integralmente nativo. Não há segunda instância do cliente.
