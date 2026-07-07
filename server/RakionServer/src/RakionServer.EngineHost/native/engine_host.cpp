// Host nativo C++ (x86, MSVC) da engine.dll (Serious Engine 1) — o "corpo" de netcode do bot headless.
// Hospeda uma sessao SE1 (StartPeerToPeer_t) com o stage CARREGADO (mundo + entidades), sem janela.
// O host .NET NAO serve: o CLR moderno nao carrega a engine.dll VC7.1 (err 126). Este e' o host de producao.
//
// 4 fixes que destravam o world-load headless (todos com RE em docs/headless-engine-re.md):
//   1. /NXCOMPAT:NO no link  -> DEP OFF: o stub ASPack da EntitiesMP executa em pagina de dados (DEP mataria).
//   2. LoadGameDlls          -> pre-carrega gamemp+EntitiesMP (registra CWorldBase/CPlayer/...).
//   3. GpaHook (IAT)         -> bridge: o class-loader pede *_DLLClass com handle NULL -> resolve na EntitiesMP.
//   4. AutoCommitVeh         -> commita sob demanda os buffers que a engine reserva (CTStream do world-load).
//   + patches GetStreamCRC32/InitCRCGather (sync de CRC, descartavel offline) e Atalho B (Open_t enche o buffer).
//
// BUILD x86 (DEP off OBRIGATORIO): cl /EHa /O2 engine_host.cpp /link /NXCOMPAT:NO  (ver build.ps1)
//   engine_host.exe <Bin> <mode=host|join|net> <gameId=Rakion> <world|hostAddr>
#include <windows.h>
#include <cstdio>
#include <cstring>

static HMODULE g_eng;
static HMODULE g_hEnt, g_hGame;   // EntitiesMP @0x35000000 + gamemp @0x10000000 (pre-carregadas)
template <class T> static T Fn(const char* n) { return reinterpret_cast<T>(GetProcAddress(g_eng, n)); }

struct CTStringV { const char* p; };  // CTString/CTFileName = { char* } no offset 0
typedef void* (__thiscall* CtorStr_t)(void* self, const char* s);
typedef void  (__cdecl*    SEInit_t)(CTStringV);
typedef int   (__thiscall* Prepare_t)(void* self, int useNet, int client);
typedef void  (__cdecl*    VoidFn_t)();
typedef void  (__thiscall* StartP2P_t)(void* self, const void* name, const void* world,
                                       unsigned long flags, long maxPlayers, int waitAll, void* props);

// Constroi um CTString/CTFileName num buffer via o ctor(const char*) exportado da engine.
static void BuildStr(const char* ctorName, void* buf, const char* s)
{
    *reinterpret_cast<void**>(buf) = nullptr;
    Fn<CtorStr_t>(ctorName)(buf, s);
}

static void* PNetwork()
{
    void* pp = GetProcAddress(g_eng, "?_pNetwork@@3PAVCNetworkLibrary@@A");
    return pp ? *reinterpret_cast<void**>(pp) : nullptr;
}

// Auto-commit: a engine RESERVA buffers grandes (CTStream/arrays do world-load) e commita paginas sob
// demanda. Headless, o commit-on-grow nao dispara -> AV-write numa pagina MEM_RESERVE. Aqui: na falha,
// se o endereco esta numa regiao RESERVE, commita a pagina e RE-EXECUTA a instrucao. So toca RESERVE
// (endereco realmente invalido -> FREE -> deixa crashar, e bug de verdade).
static volatile LONG g_autoCommit = 0;
static int g_commits = 0;
static LONG CALLBACK AutoCommitVeh(EXCEPTION_POINTERS* ep)
{
    EXCEPTION_RECORD* er = ep->ExceptionRecord;
    if (g_autoCommit && er->ExceptionCode == 0xC0000005u && er->NumberParameters >= 2) {
        void* addr = reinterpret_cast<void*>(er->ExceptionInformation[1]);
        MEMORY_BASIC_INFORMATION mbi;
        if (VirtualQuery(addr, &mbi, sizeof(mbi)) && mbi.State == MEM_RESERVE) {
            void* page = reinterpret_cast<void*>(reinterpret_cast<uintptr_t>(addr) & ~static_cast<uintptr_t>(0xFFF));
            if (VirtualAlloc(page, 0x1000, MEM_COMMIT, PAGE_READWRITE)) {
                if (g_commits++ < 8) { printf("[commit] %p (acc=%s) commitada, re-exec\n", addr, er->ExceptionInformation[0] ? "write" : "read"); fflush(stdout); }
                return EXCEPTION_CONTINUE_EXECUTION;
            }
        }
    }
    return EXCEPTION_CONTINUE_SEARCH;
}

// Carrega gamemp.dll (CGame) + EntitiesMP.dll (CWorldBase/CPlayer/...). DEVE rodar com DEP off (o processo
// e' linkado /NXCOMPAT:NO): a EntitiesMP e' ASPack e o stub de unpack executa em pagina de dados nao-exec,
// que DEP mataria (AV no entry-point 0x354C1001). EntitiesMP e' no-reloc -> mapeia na base fixa 0x35000000.
static HMODULE LoadGameDlls(const char* bin)
{
    HMODULE hEnt = 0;
    const char* dlls[2] = { "gamemp.dll", "EntitiesMP.dll" };
    for (int i = 0; i < 2; i++) {
        char dp[1024]; sprintf_s(dp, "%s\\%s", bin, dlls[i]);
        HMODULE h = LoadLibraryExA(dp, nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        printf("[c++] LoadLibrary %s = %p (err %lu)\n", dlls[i], (void*)h, h ? 0 : GetLastError());
        if (i == 0) g_hGame = h;
        if (i == 1) { hEnt = h; g_hEnt = h; }
    }
    return hEnt;
}

// Escreve bytes em .text (torna a pagina gravavel, restaura, flush do icache).
static bool Patch(unsigned va, const unsigned char* bytes, size_t n)
{
    DWORD old;
    if (!VirtualProtect(reinterpret_cast<void*>(va), n, PAGE_EXECUTE_READWRITE, &old)) return false;
    memcpy(reinterpret_cast<void*>(va), bytes, n);
    VirtualProtect(reinterpret_cast<void*>(va), n, old, &old);
    FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<void*>(va), n);
    return true;
}

// Le o NOME DO TIPO de uma excecao C++ MSVC (codigo 0xE06D7363) caminhando o ThrowInfo->
// CatchableTypeArray->CatchableType->TypeDescriptor.name (layout x86, ponteiros absolutos).
static char g_excType[512];
static char g_excChar[512];
static char g_stack[640];
static int CxxFilter(EXCEPTION_POINTERS* ep)
{
    EXCEPTION_RECORD* er = ep->ExceptionRecord;
    unsigned eip = (unsigned)ep->ContextRecord->Eip;
    sprintf_s(g_excType, "code=%08X eip=%08X (rva=%08X)", (unsigned)er->ExceptionCode, eip, eip - 0x36000000u);
    // varre a pilha por enderecos de retorno na .text da engine (0x36001000..0x36215000) = cadeia de chamada
    g_stack[0] = 0;
    strcpy_s(g_stack, "rets(rva):");
    __try {
        unsigned* sp = reinterpret_cast<unsigned*>(ep->ContextRecord->Esp);
        int found = 0;
        for (int i = 0; i < 600 && found < 16 && strlen(g_stack) < 600; i++) {
            unsigned v = sp[i];
            if (v >= 0x36001000u && v < 0x36215000u)
                { sprintf_s(g_stack + strlen(g_stack), 640 - strlen(g_stack), " %05X", v - 0x36000000u); found++; }
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) { strcat_s(g_stack, " [scan-fault]"); }
    if (er->ExceptionCode == 0xC0000005u && er->NumberParameters >= 2)
    {
        CONTEXT* c = ep->ContextRecord;
        sprintf_s(g_excType + strlen(g_excType), 512 - strlen(g_excType), " AV-%s addr=%08X edi=%08X",
                  er->ExceptionInformation[0] ? "write" : "read", (unsigned)er->ExceptionInformation[1], (unsigned)c->Edi);
        // dump dos campos de stream (CTStream/CTFileStream: +0x0C=buffer +0x14=cursor +0x18=EOF) a partir de
        // esi (this de PeekID/ExpectID) E edi (variantes). Mostra a janela mapeada vs onde o cursor parou.
        sprintf_s(g_excType + strlen(g_excType), 512 - strlen(g_excType), " esi=%08X", (unsigned)c->Esi);
        __try {
            unsigned* o = reinterpret_cast<unsigned*>(c->Esi);
            sprintf_s(g_excType + strlen(g_excType), 512 - strlen(g_excType), " S[+0=%08X +c=%08X +14=%08X +18=%08X]",
                      o[0], o[3], o[5], o[6]);
        } __except (EXCEPTION_EXECUTE_HANDLER) { strcat_s(g_excType, " [esi-fault]"); }
        // ebp + arg0 ([ebp+8]) + campos de arg0: identifica o objeto cujo field4 e' NULL (game-state do CGame).
        sprintf_s(g_excType + strlen(g_excType), 512 - strlen(g_excType), " ebp=%08X ecx=%08X", (unsigned)c->Ebp, (unsigned)c->Ecx);
        __try {
            unsigned arg0 = *reinterpret_cast<unsigned*>(c->Ebp + 8);
            unsigned* a = reinterpret_cast<unsigned*>(arg0);
            sprintf_s(g_excType + strlen(g_excType), 512 - strlen(g_excType), " arg0=%08X A[+0=%08X +4=%08X +8=%08X +c=%08X]",
                      arg0, a[0], a[1], a[2], a[3]);
        } __except (EXCEPTION_EXECUTE_HANDLER) { strcat_s(g_excType, " [arg0-fault]"); }
    }
    if (er->ExceptionCode == 0xE06D7363u && er->NumberParameters >= 3)
    {
        unsigned* info = reinterpret_cast<unsigned*>(er->ExceptionInformation);
        void* obj = reinterpret_cast<void*>(info[1]);                 // objeto lancado
        unsigned* ti = reinterpret_cast<unsigned*>(info[2]);          // _ThrowInfo
        __try {
            unsigned* cta = reinterpret_cast<unsigned*>(ti[3]);       // pCatchableTypeArray
            if (cta && cta[0] >= 1) {
                unsigned* ct = reinterpret_cast<unsigned*>(cta[1]);   // 1o CatchableType
                char* td = reinterpret_cast<char*>(ct[1]);           // TypeDescriptor
                sprintf_s(g_excType + strlen(g_excType), 512 - strlen(g_excType), " type=%s", td + 8);
            }
            // se for char*/const char*, o objeto e' um ponteiro-p/-char* -> tenta ler a mensagem
            if (obj) { char* s = *reinterpret_cast<char**>(obj); if (s) strncpy_s(g_excChar, s, 400); }
        } __except (EXCEPTION_EXECUTE_HANDLER) {}
    }
    return EXCEPTION_EXECUTE_HANDLER;
}

typedef void (__thiscall* StartP2P2_t)(void*, const void*, const void*, unsigned long, long, int, void*);
static int CallStartP2P_SEH(void* fn, void* pNet, void* name, void* world, void* props)
{
    g_excType[0] = g_excChar[0] = 0;
    __try { reinterpret_cast<StartP2P2_t>(fn)(pNet, name, world, 0, 4, 0, props); return 0; }
    __except (CxxFilter(GetExceptionInformation())) { return -1; }
}

// JOIN (cliente): JoinSession_t da variante Rakion = thiscall (CNetworkSession const& ns, long ctLocalPlayers,
// CTFileName fnm POR VALOR). O CTFileName por valor e' um struct {char*} de 4B = um push. O handshake
// (Start_AtClient_t) bloqueia bombeando ate as respostas do host (VTAG->STATEDELTA->CRC), entao isto so volta
// quando o join completa (ou lanca). Auto-commit ON: o joiner carrega o `.wld` LOCAL (NET_MakeDefaultState).
struct CTFileNameVal { void* p; };
typedef void (__thiscall* Join3_t)(void* self, const void* ns, long ctLocal, CTFileNameVal fnm);
static int CallJoinSEH(void* fn, void* pNet, void* ns, long ctLocal, void* fnmCharPtr)
{
    g_excType[0] = g_excChar[0] = 0;
    CTFileNameVal v; v.p = fnmCharPtr;
    __try { reinterpret_cast<Join3_t>(fn)(pNet, ns, ctLocal, v); return 0; }
    __except (CxxFilter(GetExceptionInformation())) { return -1; }
}

// AddPlayer_t(CPlayerCharacter&) -> CPlayerSource*: registra um jogador LOCAL na sessao (no host = jogador
// IA do bot). Internamente CPlayerSource::Start_t manda MSG_REQ_CONNECTPLAYER (serializa o CPlayerCharacter)
// e espera MSG_REP_CONNECTPLAYER com o pls_Index (loopback no host). CPlayerCharacter (variante Rakion):
// ctor (CTString nome, CTString time); layout GUID16+name+team+appearance32.
// SEH p/ chamada cdecl sem-arg que retorna void* (ex.: GAME_Create).
static void* g_genResult;
typedef void* (__cdecl* CdeclRet_t)();
static int CallCdeclSEH(void* fn)
{
    g_excType[0] = g_excChar[0] = 0; g_genResult = nullptr;
    __try { g_genResult = reinterpret_cast<CdeclRet_t>(fn)(); return 0; }
    __except (CxxFilter(GetExceptionInformation())) { return -1; }
}

// SEH p/ chamada thiscall void(this) (ex.: CGame::InitInternal).
typedef void (__thiscall* ThisVoidArg_t)(void*);
static int CallThisVoidSEH(void* fn, void* self)
{
    g_excType[0] = g_excChar[0] = 0;
    __try { reinterpret_cast<ThisVoidArg_t>(fn)(self); return 0; }
    __except (CxxFilter(GetExceptionInformation())) { return -1; }
}

typedef void* (__thiscall* AddPlayer2_t)(void* self, void* pc);
static void* g_plsResult;
static int CallAddPlayerSEH(void* fn, void* pNet, void* pc)
{
    g_excType[0] = g_excChar[0] = 0; g_plsResult = nullptr;
    __try { g_plsResult = reinterpret_cast<AddPlayer2_t>(fn)(pNet, pc); return 0; }
    __except (CxxFilter(GetExceptionInformation())) { return -1; }
}

// Bombeia a sessao pra MANTER VIVA (servir joiners + simular). Pela fonte SE1: o que aceita joiner e simula
// e' o `ServerLoop` rodado pelo `TimerLoop`, que e' disparado por `CTimer::HandleTimerHandlers` (registrado no
// StartPeerToPeer via AddTimerHandler). TickQuantum=1/20s -> pump a ~50ms (20Hz). `MainLoop` e' o loop por-FRAME
// (prediction/game-stream lado-cliente) e CRASHA headless (deref do global de render @0x3636f260 nulo) -> NAO o
// chamamos no host dedicado. SEH protege (AV/throw aqui = diagnostico, nao crash silencioso).
typedef void (__thiscall* ThisVoid_t)(void*);
static int PumpSEH(void* pNet, void* pTimer, ThisVoid_t mainLoop, ThisVoid_t handleTimers, int secs)
{
    g_excType[0] = g_excChar[0] = 0;
    __try {
        int ticks = secs * 20;
        for (int i = 0; i < ticks; i++) {
            if (pTimer && handleTimers) handleTimers(pTimer);   // -> TimerLoop -> ServerLoop (aceita joiner+sim)
            if (mainLoop) mainLoop(pNet);                       // opcional (off no host: crasha no render headless)
            if (i % 40 == 0) { printf("[pump] t=%ds vivo\n", i / 20); fflush(stdout); }
            Sleep(50);
        }
        return 0;
    } __except (CxxFilter(GetExceptionInformation())) { return -1; }
}

// ---- Atalho B: hook do CTFileStream::Open_t @0x3603e920. O Open_t reserva o buffer do arquivo mas o read
//      e' lazy e NAO dispara headless -> buffer reservado-vazio -> AV. Apos o Open_t, enchemos o buffer com
//      o arquivo decodificado do XFS (pre-extraido em C:\temp\xfs_ext). ----
static FILE* g_openlog;
static void* g_openTramp;
typedef void (__thiscall* OpenOrig_t)(void* self, const void* fnm, int om);
static OpenOrig_t g_openOrig;   // = trampoline (7 bytes roubados + jmp Open_t+7)

// ATALHO B: preenche o buffer do CTFileStream com o arquivo decodificado do XFS (pre-extraido em
// C:\temp\xfs_ext). O Open_t (XFS nem loose) NAO le o conteudo — so reserva o buffer; o read lazy nao dispara
// headless. Aqui, APOS o Open_t, committamos o buffer reservado e copiamos os bytes. Campos do CTFileStream:
// +0xC=buffer +0x18=eof +0x64=xFile (!=0 => veio do XFS).
static void FillBufferFromXfs(void* self, const void* fnm)
{
    __try {
        unsigned* s = reinterpret_cast<unsigned*>(self);
        unsigned char* buf = reinterpret_cast<unsigned char*>(s[0x0C / 4]);
        unsigned eof = s[0x18 / 4];
        void* xFile = reinterpret_cast<void*>(s[0x64 / 4]);
        if (!buf) return;
        unsigned size = eof - reinterpret_cast<unsigned>(buf);
        if (size == 0 || size > 0x8000000) return;
        const char* path = *reinterpret_cast<const char* const*>(fnm);   // CTFileName[0] = char*
        if (!path || !*path) return;
        char full[1024]; sprintf_s(full, "C:\\temp\\xfs_ext\\%s", path);
        FILE* f = fopen(full, "rb");
        if (!f) { if (g_openlog) { fprintf(g_openlog, "[fill] SEM extraido (%s) buf=%p size=%u xFile=%p\n", path, buf, size, xFile); fflush(g_openlog); } return; }
        VirtualAlloc(buf, size, MEM_COMMIT, PAGE_READWRITE);              // committa o buffer reservado
        size_t rd = fread(buf, 1, size, f);
        fclose(f);
        if (g_openlog) { fprintf(g_openlog, "[fill] %s -> buf=%p size=%u rd=%u xFile=%p\n", path, buf, size, (unsigned)rd, xFile); fflush(g_openlog); }
    } __except (EXCEPTION_EXECUTE_HANDLER) { if (g_openlog) { fprintf(g_openlog, "[fill] excecao\n"); fflush(g_openlog); } }
}

static void __fastcall OpenWrapperImpl(void* self, void* /*edx*/, const void* fnm, int om)
{
    g_openOrig(self, fnm, om);      // roda o Open_t original (acha o arquivo, reserva o buffer)
    FillBufferFromXfs(self, fnm);   // preenche o buffer com o conteudo decodificado
}

static __declspec(naked) void OpenHook()
{
    // entrada do Open_t: ecx=this, [esp]=retaddr, [esp+4]=fnm, [esp+8]=om
    __asm {
        mov eax, [esp + 8]      // om
        push eax
        mov eax, [esp + 8]      // fnm (apos o push)
        push eax
        call OpenWrapperImpl    // __fastcall: ecx=this(ja), stack=fnm,om (callee limpa 8)
        ret 8                   // limpa os args originais + retorna ao caller
    }
}
static void InstallOpenHook()
{
    const unsigned VA = 0x3603e920u;
    g_openTramp = VirtualAlloc(0, 16, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    unsigned char* tr = reinterpret_cast<unsigned char*>(g_openTramp);
    memcpy(tr, reinterpret_cast<void*>(VA), 7);                 // rouba 7 bytes (push -1 + push imm32)
    tr[7] = 0xE9; *reinterpret_cast<unsigned*>(tr + 8) = (VA + 7) - (reinterpret_cast<unsigned>(tr) + 12);
    g_openOrig = reinterpret_cast<OpenOrig_t>(g_openTramp);   // chama o Open_t original via o trampolim
    DWORD old;
    VirtualProtect(reinterpret_cast<void*>(VA), 7, PAGE_EXECUTE_READWRITE, &old);
    unsigned char* p = reinterpret_cast<unsigned char*>(VA);
    p[0] = 0xE9; *reinterpret_cast<unsigned*>(p + 1) = reinterpret_cast<unsigned>(&OpenHook) - (VA + 5);
    p[5] = 0x90; p[6] = 0x90;
    VirtualProtect(reinterpret_cast<void*>(VA), 7, old, &old);
    FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<void*>(VA), 7);
    printf("[c++] hook Open_t @0x%08X instalado (log em C:\\temp\\engine_opens.log)\n", VA);
}

// ---- Patch de IAT da engine.dll: intercepta SO as chamadas da engine a GetProcAddress (alvo limpo, sem
//      hook global de kernel32). O class-loader da engine resolve os exports "<Classe>_DLLClass" com handle
//      NULL — no jogo original a EntitiesMP e' import ESTATICO da rakion.exe protegida, entao a engine
//      assume o modulo no proprio exe; headless nao ha esse modulo. Bridge: pedido de *_DLLClass com handle
//      nulo -> resolve contra a EntitiesMP/gamemp ja carregadas (LoadGameDlls). ----
typedef FARPROC (WINAPI* GPA_t)(HMODULE, LPCSTR);
static GPA_t g_gpaOrig;

static FARPROC WINAPI GpaHook(HMODULE h, LPCSTR name)
{
    bool isClass = reinterpret_cast<unsigned>(name) > 0xFFFF && strstr(name, "_DLLClass");
    if (isClass && !h) {
        FARPROC r = g_gpaOrig(g_hEnt, name);
        if (!r && g_hGame) r = g_gpaOrig(g_hGame, name);
        return r;
    }
    return g_gpaOrig ? g_gpaOrig(h, name) : GetProcAddress(h, name);
}

static void* PatchIat(HMODULE mod, const char* func, void* repl)
{
    unsigned char* base = reinterpret_cast<unsigned char*>(mod);
    IMAGE_DOS_HEADER* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    IMAGE_NT_HEADERS* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    IMAGE_DATA_DIRECTORY imp = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!imp.VirtualAddress) return nullptr;
    IMAGE_IMPORT_DESCRIPTOR* id = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + imp.VirtualAddress);
    for (; id->Name; id++) {
        IMAGE_THUNK_DATA* oft = reinterpret_cast<IMAGE_THUNK_DATA*>(base + id->OriginalFirstThunk);
        IMAGE_THUNK_DATA* ft  = reinterpret_cast<IMAGE_THUNK_DATA*>(base + id->FirstThunk);
        if (!id->OriginalFirstThunk) oft = ft;
        for (; oft->u1.AddressOfData; oft++, ft++) {
            if (oft->u1.Ordinal & IMAGE_ORDINAL_FLAG32) continue;
            IMAGE_IMPORT_BY_NAME* ibn = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + oft->u1.AddressOfData);
            if (strcmp(reinterpret_cast<char*>(ibn->Name), func) == 0) {
                void* orig = reinterpret_cast<void*>(ft->u1.Function);
                DWORD old; VirtualProtect(&ft->u1.Function, sizeof(void*), PAGE_READWRITE, &old);
                ft->u1.Function = reinterpret_cast<ULONGLONG>(repl);
                VirtualProtect(&ft->u1.Function, sizeof(void*), old, &old);
                return orig;
            }
        }
    }
    return nullptr;
}

int main(int argc, char** argv)
{
    g_openlog = fopen("C:\\temp\\engine_opens.log", "w");
    const char* bin   = argc > 1 ? argv[1] : "C:\\Users\\joaop\\Desenvolvimento\\Rakion\\rakion-final\\Bin";
    const char* mode  = argc > 2 ? argv[2] : "host";
    const char* gameId= argc > 3 ? argv[3] : "Rakion";
    const char* arg4  = argc > 4 ? argv[4] : "Levels\\TechTest.wld";  // world (host) ou hostAddr (join)

    char dll[1024];      sprintf_s(dll, "%s\\engine.dll", bin);
    char dataRoot[1024]; strcpy_s(dataRoot, bin);
    if (char* sl = strrchr(dataRoot, '\\')) *sl = 0;   // pai do Bin = data-root dos .xfs

    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    SetDllDirectoryA(bin);
    g_eng = LoadLibraryExA(dll, nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    printf("[c++] mode=%s engine=%p\n", mode, (void*)g_eng);
    if (!g_eng) { printf("[c++] LoadLibrary FALHOU %lu\n", GetLastError()); return 2; }

    if (int* ded = reinterpret_cast<int*>(GetProcAddress(g_eng, "?_bDedicatedServer@@3HA")))
        { *ded = 1; printf("[c++] _bDedicatedServer=1 (no-render)\n"); }

    AddVectoredExceptionHandler(1, AutoCommitVeh); // auto-commit de buffers reservados (gated por g_autoCommit)
    g_gpaOrig = reinterpret_cast<GPA_t>(PatchIat(g_eng, "GetProcAddress", reinterpret_cast<void*>(&GpaHook)));

    // Pre-carrega gamemp + EntitiesMP AGORA (antes do SE_InitEngine): registra as classes de entidade que o
    // world-load instancia. O Obtain do world-load reusa o handle ja carregado via o bridge do GpaHook.
    HMODULE hEntEarly = LoadGameDlls(bin);

    // Neutraliza CTStream::GetStreamCRC32_t @0x3603C660 (xor eax,eax; ret): no headless o .wld vem do .xfs
    // num CTMemoryStream com EOF>buffer -> o CRC-gather do world-load lia alem do buffer e dava AV. O CRC
    // so compara hash de mundo cliente<->servidor (descartavel offline). Causa-raiz: RE em [[headless-engine-host]].
    { const unsigned char retzero[] = { 0x33, 0xC0, 0xC3 };
      printf("[c++] patch GetStreamCRC32->ret0 @0x3603C660: %s\n", Patch(0x3603C660u, retzero, 3) ? "ok" : "FALHOU"); }

    // Neutraliza CNetworkLibrary::InitCRCGather @0x360F41E0 (void thiscall -> ret). O call-chain do AV (via
    // walk de pilha) provou: StartPeerToPeer -> InitCRCGather -> Obtain CEntityClass -> CSerial::Load_t ->
    // CEntityClass::Read_t -> ReadFromText_t -> AV num stream VAZIO. A coleta de CRC das deps do mundo é
    // sync cliente<->servidor, DESCARTAVEL offline. Patchar a func inteira pula o gather e vai ao world-load.
    { const unsigned char ret1[] = { 0xC3 };
      printf("[c++] patch InitCRCGather->ret @0x360F41E0: %s\n", Patch(0x360F41E0u, ret1, 1) ? "ok" : "FALHOU"); }

    // Pula o bloco periodico (gated 100ms) em 0x3610CDB0 que faz uma atualizacao de status/UI derefando o
    // render-global @0x3636f260 (NULL headless -> AV no virtual-call @0x3610CDE1). NAO e' processamento de rede
    // (0x360efe30 chamado antes e' so um getter `mov eax,[ecx+0x2c];ret`); o resto da funcao continua em
    // 0x3610CDF4. Reached pelo MainLoop E pelo pump interno do handshake do JoinSession. Patch: jb->jmp em
    // 0x3610CDC9 (0x72->0xEB) = pula sempre o bloco do render-global.
    { const unsigned char jmp[] = { 0xEB };
      printf("[c++] patch render-status skip @0x3610CDC9 (jb->jmp): %s\n", Patch(0x3610CDC9u, jmp, 1) ? "ok" : "FALHOU"); }

    // EXPERIMENTO: bypass do scramble/hash 0x3600C510 que trava no appearance-placeholder do AddPlayer.
    // A func preenche um buffer com keystream do RNG indexado por [appearance+8] (ponteiro headless -> OOB).
    // Patch do jbe @0x3600C522 (76->EB) = pula SEMPRE o loop -> so faz cleanup+ret. Se o scramble for
    // descartavel (como GetStreamCRC32/InitCRCGather), o AddPlayer passa. Ver docs/headless-engine-re.md §13.
    { const unsigned char jmp[] = { 0xEB };
      printf("[c++] patch scramble-skip @0x3600C522 (jbe->jmp): %s\n", Patch(0x3600C522u, jmp, 1) ? "ok" : "FALHOU"); }

    InstallOpenHook();   // loga cada arquivo aberto (diagnostico do path do world-load)

    SetCurrentDirectoryA(dataRoot);
    printf("[c++] CWD(data-root)=%s\n", dataRoot);

    CTStringV gid; { char b[8] = {0}; BuildStr("??0CTString@@QAE@PBD@Z", b, gameId); gid.p = *reinterpret_cast<const char**>(b); }
    printf("[c++] SE_InitEngine(\"%s\")...\n", gameId); fflush(stdout);
    Fn<SEInit_t>("?SE_InitEngine@@YAXVCTString@@@Z")(gid);

    void* pNet = PNetwork();
    printf("[c++] _pNetwork=%p %s\n", pNet, pNet ? "*** NETCODE UP" : "(null)");
    if (!pNet) return 5;

    // SE1 exige stream-handling por-thread p/ I/O de arquivo (load de .ecl/.wld). O SE_InitEngine o
    // desabilita ao retornar -> reabilitar nesta thread antes de StartPeerToPeer/JoinSession (que
    // carregam Classes\Player.ecl + o mundo). Sem isto: "stream handling is not enabled for this thread".
    Fn<VoidFn_t>("?EnableStreamHandling@CTStream@@SAXXZ")();
    printf("[c++] CTStream::EnableStreamHandling() OK (I/O de arquivo liberado nesta thread)\n");

    void* cmi = GetProcAddress(g_eng, "?_cmiComm@@3VCCommunicationInterface@@A");  // V=objeto, addr=this
    bool client = (strcmp(mode, "join") == 0);
    int r = Fn<Prepare_t>("?PrepareForUse@CCommunicationInterface@@QAEHHH@Z")(cmi, 1, client ? 1 : 0);
    printf("[c++] PrepareForUse(useNet=1, client=%d)=%d\n", client ? 1 : 0, r);
    if (strcmp(mode, "net") == 0) return 0;

    if (!hEntEarly) { printf("[c++] EntitiesMP nao carregou (DEP on? falta /NXCOMPAT:NO)\n"); return 6; }

    // Cria o objeto CGame (gamemp `GAME_Create`) e seta o global do engine @0x3636F260 = gerencia de
    // jogadores/personagens (vtable[+8] devolve o array de players, stride 0x378). O init completo do jogo
    // faz isso; sem ele todo o caminho de player (CPlayerSource/AddPlayer + a UI de connect do cliente)
    // deref NULL. Auto-commit ON (CGame aloca buffers reservados).
    InterlockedExchange(&g_autoCommit, 1);
    {
        void** pGameGlobal = reinterpret_cast<void**>(0x3636F260u);
        void* gc = (void*)GetProcAddress(g_hGame, "?GAME_Create@@YAPAVCGame@@XZ");
        printf("[c++] GAME_Create=%p, global[0x3636F260] antes=%p\n", gc, *pGameGlobal); fflush(stdout);
        if (gc && CallCdeclSEH(gc) == 0) {
            void* pGame = g_genResult;
            printf("[c++] GAME_Create -> CGame=%p, global depois=%p\n", pGame, *pGameGlobal);
            if (!*pGameGlobal && pGame) { *pGameGlobal = pGame; printf("[c++] global do CGame setado manualmente\n"); }
            // CGame::InitInternal @ gamemp+0x13AE0 (RE: seeds membros + ~86 DeclareSymbol + AddTimerHandler +
            // include do startup-script (monta player-controls) + LCDInit). Sem ele o player-state fica vazio e o
            // AddPlayer derefa NULL. thiscall void(this). _bDedicatedServer=1 pula o include de persistent-symbols.
            if (pGame) {
                void* initFn = reinterpret_cast<char*>(g_hGame) + 0x13AE0;
                printf("[c++] CGame::InitInternal(@%p)...\n", initFn); fflush(stdout);
                int ir = CallThisVoidSEH(initFn, pGame);
                if (ir == 0) printf("[c++] CGame::InitInternal OK *** game/player-system inicializado\n");
                else         printf("[c++] >>> EXCECAO no InitInternal: %s | '%s'\n   %s\n", g_excType, g_excChar, g_stack);
            }
            // NOTA: o player-creation deref globals de game-state (ex.: arg0=0x3636F75C, e o singleton 0x3636F338)
            // que ficam ZERADOS headless — sao construidos pela init de MATCH/game-mode do jogo, que depende das
            // session-properties (2048B, Rakion-especifico) passadas ao StartPeerToPeer. Aqui passo props zeradas =>
            // game-mode nao sobe. Construir o singleton via accessor 0x361986E0 NAO basta (0x3636F75C e' separado).
            // Ver docs/headless-engine-re.md §11/§12.
        } else printf("[c++] >>> GAME_Create falhou/excecao: %s\n   %s\n", g_excType, g_stack);
    }

    if (strcmp(mode, "host") == 0)
    {
        char nameBuf[8] = {0}, worldBuf[8] = {0};
        char props[2048] = {0};
        BuildStr("??0CTString@@QAE@PBD@Z",   nameBuf,  "BotHost");
        BuildStr("??0CTFileName@@QAE@PBD@Z", worldBuf, arg4);
        void* startP2P = (void*)GetProcAddress(g_eng, "?StartPeerToPeer_t@CNetworkLibrary@@QAEXABVCTString@@ABVCTFileName@@KJHPAX@Z");
        InterlockedExchange(&g_autoCommit, 1);   // liga o auto-commit dos buffers reservados do world-load
        printf("[c++] StartPeerToPeer_t(world=\"%s\") em SEH-probe (auto-commit ON)...\n", arg4); fflush(stdout);
        int rc = CallStartP2P_SEH(startP2P, pNet, nameBuf, worldBuf, props);
        if (rc != 0) { printf("[c++] >>> EXCECAO: %s | msg-se-char*: '%s'\n   %s\n", g_excType, g_excChar, g_stack); return 7; }
        printf("[c++] StartPeerToPeer RETORNOU OK *** world-load headless FUNCIONOU\n");

        // Registra 1 bot como jogador LOCAL (IA-dirigido). De-risk do modelo (b): bot existe como peer no stage.
        {
            // CPlayerCharacter = 0x370 bytes (stride do array de players no AddPlayer); buffer precisa caber tudo.
            char pcName[8] = {0}, pcTeam[8] = {0}, pcBuf[0x400] = {0};
            BuildStr("??0CTString@@QAE@PBD@Z", pcName, "Bot1");
            BuildStr("??0CTString@@QAE@PBD@Z", pcTeam, "");
            typedef void* (__thiscall* PcCtor2_t)(void*, const void*, const void*);
            reinterpret_cast<PcCtor2_t>(GetProcAddress(g_eng, "??0CPlayerCharacter@@QAE@ABVCTString@@0@Z"))(pcBuf, pcName, pcTeam);
            // NOTA: a aparencia (appearance@0x18) era pista falsa — o crash do AddPlayer e' ANTES, no servidor
            // criando o CPlayerEntity (SessionState MSG_SEQ_ADDPLAYER -> CWorld::CreateEntity("Classes\Player.ecl")
            // -> pecClass->New() = ctor do CPlayer do entitiesmp, que chama o helper engine 0x36017E60 e le um
            // sub-objeto de game-state NULL). Causa provavel: CGame::InitInternal nao completou (startup-script
            // generico, "Cannot load game settings"). Ver docs/headless-engine-re.md §10/§11.
            // DIAGNOSTICO (captura 2026-06-28): no cliente real (host) _pNetwork->[0x14]==0 -> Start_t cai no
            // caminho LOCAL @0x3610371b (usa CGame->vtable[+8]); se [0x14]!=0 cai no caminho cliente que copia o
            // CPlayerCharacter do jogador LOCAL (global @0x3636F40C, ZERADO headless) -> AV no copy-helper 0x36017E50.
            {
                unsigned f14  = *reinterpret_cast<unsigned*>(reinterpret_cast<char*>(pNet) + 0x14);
                unsigned f24  = *reinterpret_cast<unsigned*>(reinterpret_cast<char*>(pNet) + 0x24);
                void* cg      = *reinterpret_cast<void**>(0x3636F260u);
                unsigned loc0 = *reinterpret_cast<unsigned*>(0x3636F40Cu);  // base do CPlayerCharacter "local" (edi)
                unsigned loc1 = *reinterpret_cast<unsigned*>(0x3636F760u);  // src->field4 que crasha (+0x350+4)
                printf("[c++] DIAG pNet->[0x14]=%08X (0=host/local, !=0=cliente)  pNet->[0x24]=%08X  CGame=%p\n",
                       f14, f24, cg);
                printf("[c++] DIAG global localChar@0x3636F40C[0]=%08X  field4@0x3636F760=%08X (0=>copy crasha)\n",
                       loc0, loc1);
                // LAYOUT do CPlayerCharacter construido (dwords NAO-zero) -> mostra onde vivem os CTString (name/team)
                // e o que falta (appearance/model). O crash le src->[+0x354].
                unsigned* pcw = reinterpret_cast<unsigned*>(pcBuf);
                printf("[c++] DIAG pcBuf non-zero dwords:");
                for (int k = 0; k < 0x370 / 4; k++) if (pcw[k]) printf(" +%X=%08X", k * 4, pcw[k]);
                printf("\n"); fflush(stdout);
            }
            // EXPERIMENTO: o caminho cliente copia o CPlayerCharacter LOCAL do global 0x3636F40C (zerado => crash).
            // Popula esse global com o CPlayerCharacter que construimos (name/team validos) e ve se o crash anda.
            memcpy(reinterpret_cast<void*>(0x3636F40Cu), pcBuf, 0x370);
            printf("[c++] EXPERIMENTO: pcBuf -> localChar global 0x3636F40C (0x370B) copiado\n"); fflush(stdout);

            // Constroi o singleton RNG global 0x3636F338 via o accessor 0x361986E0 (Meyers lazy-init): o
            // construtor 0x36198400 ALOCA as tabelas +0x20/+0x24/+0x28. Headless o accessor nunca foi chamado
            // (o caller do hash usa o endereco estatico assumindo init) -> [+0x28] lixo -> AV em 0x3600C52F.
            {
                typedef void* (__cdecl* SingletonAcc_t)();
                unsigned char* guard = reinterpret_cast<unsigned char*>(0x3636F38Cu);   // bit0 = "ja construido"
                unsigned* s30a = reinterpret_cast<unsigned*>(0x3636F338u + 0x30);
                unsigned* s28a = reinterpret_cast<unsigned*>(0x3636F338u + 0x28);
                printf("[c++] RNG antes: guard=%02X [+0x28]=%08X [+0x30]=%08X\n", *guard, *s28a, *s30a);
                *guard = 0;   // limpa o guard -> forca reconstrucao completa (semeia tabelas + [+0x30])
                __try {
                    void* rng = reinterpret_cast<SingletonAcc_t>(0x361986E0u)();
                    printf("[c++] RNG reconstruido=%p  guard=%02X [+0x28]=%08X [+0x30]=%08X\n", rng, *guard, *s28a, *s30a);
                } __except (EXCEPTION_EXECUTE_HANDLER) { printf("[c++] accessor RNG lancou (segue)\n"); }
                fflush(stdout);
            }
            // NOTA (RE 2026-07-06): a cascata do AddPlayer converge no APPEARANCE. O ctor aponta
            // CPlayerCharacter+0x18 -> 0x36290A60, que em runtime tem VALORES-SENTINELA (0x56341200/0x12340000/
            // 0x90125678 = padrao 0x12 34 56 78 90...) = PLACEHOLDER de debug, so preenchido quando um CLIENTE REAL
            // conecta (MSG_REQ_CONNECTPLAYER traz o appearance serializado). Headless-sozinho nao tem esse dado ->
            // a serializacao (hash de 0x3601a2b0 sobre [appearance+8]) le lixo/ponteiro -> AV em 0x3600C52F.
            // ⇒ RESOLUCAO = H3.5 (humano joina o host, traz o appearance real). Ver docs/headless-engine-re.md §13.
            // NOTA (RE 2026-07-07): os DOIS caminhos do AddPlayer precisam do INIT COMPLETO do jogo que o
            // headless pula. [0x14]=1 (cliente) -> copia o CPlayerCharacter local (sub-objetos NULL @+0x350 ->
            // AV no copy-helper 0x36017E50). [0x14]=0 (host) -> CGame->vtable[+8] (getter do array de players) é
            // STUB `ret` @0x10011bd0 -> retorna lixo -> AV em 0x36103758: falta CGame::Initialize (aloca slots).
            // O bypass do scramble (0x3600C522) furou 1 rung; o resto é a sequência de init de partida (H3.5 traz
            // o char real; CGame::Initialize aloca os slots). Ver docs/headless-engine-re.md §13.
            void* addFn = (void*)GetProcAddress(g_eng, "?AddPlayer_t@CNetworkLibrary@@QAEPAVCPlayerSource@@AAVCPlayerCharacter@@@Z");
            printf("[c++] AddPlayer_t(\"Bot1\") em SEH-probe...\n"); fflush(stdout);
            int ar = CallAddPlayerSEH(addFn, pNet, pcBuf);
            if (ar == 0) printf("[c++] AddPlayer_t OK -> CPlayerSource=%p *** BOT registrado como peer local!\n", g_plsResult);
            else         printf("[c++] >>> EXCECAO no AddPlayer: %s | '%s'\n   %s\n", g_excType, g_excChar, g_stack);
        }

        // MANTEM A SESSAO VIVA: bombeia o main loop p/ servir joiners + timers (senao o host sai e morre).
        void* ppT = GetProcAddress(g_eng, "?_pTimer@@3PAVCTimer@@A");
        void* pTimer = ppT ? *reinterpret_cast<void**>(ppT) : nullptr;
        ThisVoid_t mainLoop = reinterpret_cast<ThisVoid_t>(GetProcAddress(g_eng, "?MainLoop@CNetworkLibrary@@QAEXXZ"));
        ThisVoid_t handleTimers = reinterpret_cast<ThisVoid_t>(GetProcAddress(g_eng, "?HandleTimerHandlers@CTimer@@QAEXXZ"));
        int secs = argc > 5 ? atoi(argv[5]) : 30;
        (void)mainLoop;   // host dedicado: ServerLoop via timer aceita o joiner; MainLoop (render) fica off
        printf("[c++] host VIVO, bombeando %ds (pTimer=%p)...\n", secs, pTimer); fflush(stdout);
        int pr = PumpSEH(pNet, pTimer, nullptr, handleTimers, secs);
        if (pr == 0) printf("[c++] pump terminou estavel (sessao ficou viva %ds)\n", secs);
        else         printf("[c++] >>> EXCECAO no pump: %s\n   %s\n", g_excType, g_stack);
    }
    else if (strcmp(mode, "join") == 0)
    {
        // join mode: arg4 = endereco do host (ex. "127.0.0.1"), argv[5] = mundo (mesmo do host).
        const char* hostAddr = arg4;
        const char* world = argc > 5 ? argv[5] : "LevelsSV\\ko2\\ko2.wld";
        char addrStr[8] = {0}, worldBuf[8] = {0}, nsBuf[512] = {0};
        BuildStr("??0CTString@@QAE@PBD@Z",   addrStr,  hostAddr);
        BuildStr("??0CTFileName@@QAE@PBD@Z", worldBuf, world);
        typedef void* (__thiscall* NsCtor_t)(void* self, const void* addr, long j);
        reinterpret_cast<NsCtor_t>(GetProcAddress(g_eng, "??0CNetworkSession@@QAE@ABVCTString@@J@Z"))(nsBuf, addrStr, 0);
        void* joinFn = (void*)GetProcAddress(g_eng, "?JoinSession_t@CNetworkLibrary@@QAEXABVCNetworkSession@@JVCTFileName@@@Z");
        void* fnmCharPtr = *reinterpret_cast<void**>(worldBuf);   // CTFileName[0] = char* (passado por valor)
        InterlockedExchange(&g_autoCommit, 1);
        printf("[c++] JoinSession_t(host=\"%s\", world=\"%s\") em SEH-probe (auto-commit ON)...\n", hostAddr, world); fflush(stdout);
        int rc = CallJoinSEH(joinFn, pNet, nsBuf, 1, fnmCharPtr);
        if (rc != 0) { printf("[c++] >>> EXCECAO no join: %s | msg-se-char*: '%s'\n   %s\n", g_excType, g_excChar, g_stack); return 8; }
        printf("[c++] JoinSession RETORNOU OK *** o peer headless ENTROU na sessao + carregou o mundo\n");

        // pump pra manter o joiner sincronizado
        void* ppT = GetProcAddress(g_eng, "?_pTimer@@3PAVCTimer@@A");
        void* pTimer = ppT ? *reinterpret_cast<void**>(ppT) : nullptr;
        ThisVoid_t handleTimers = reinterpret_cast<ThisVoid_t>(GetProcAddress(g_eng, "?HandleTimerHandlers@CTimer@@QAEXXZ"));
        int secs = argc > 6 ? atoi(argv[6]) : 10;
        printf("[c++] joiner VIVO, bombeando %ds...\n", secs); fflush(stdout);
        PumpSEH(pNet, pTimer, nullptr, handleTimers, secs);
    }
    fflush(stdout);
    return 0;
}
