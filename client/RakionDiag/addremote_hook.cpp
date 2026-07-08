// DLL de DIAGNÓSTICO (dev-only, RE de interoperabilidade) — hook inline em
// engine.dll!CSessionState::AddRemotePlayer @0x3610e2b0 (base fixa 0x36000000, sem ASLR).
//
// Assinatura (mangled ?AddRemotePlayer@CSessionState@@QAEXEGPAD@Z):
//   void __thiscall AddRemotePlayer(uchar seat, ushort blobLen, char* blob)
//   thiscall: ecx=CSessionState*; stack [esp+4]=seat [esp+8]=blobLen [esp+0xC]=blob.
//
// É a função que CRIA o combatente remoto real (equivalente Rakion do MSG_SEQ_ADDPLAYER da SE1:
// SessionState.cpp:1317 CreateEntity_t("Classes\\Player.ecl") + en_pcCharacter=pcCharacter). O blob é
// o CPlayerCharacter serializado (nome + appearance + template). Capturar (seat, blobLen, blob) quando
// o 2º jogador REAL entra dá o INSUMO p/ sintetizar a mensagem server-side (docs headless-engine-re §22.7).
//
// O caller (return addr) aponta pro rakion.exe DESCOMPRIMIDO em runtime — cravamos o site do packer
// que estática não alcança (0 refs ao IAT 0x4d01f4). Grava em C:\temp\addremote_hook.log.
#include <windows.h>
#include <cstdio>

static const DWORD ADDREMOTE = 0x3610e2b0;
static const unsigned char ORIG_PROLOGUE[6] = { 0x81, 0xEC, 0xE8, 0x09, 0x00, 0x00 }; // sub esp,0x9e8
static void* g_trampoline = nullptr;
static FILE* g_log = nullptr;
static int g_calls = 0;

static void HexDump(const unsigned char* p, unsigned n)
{
    for (unsigned i = 0; i < n; i += 16) {
        fprintf(g_log, "    %04x: ", i);
        for (unsigned j = 0; j < 16; j++)
            if (i + j < n) fprintf(g_log, "%02x ", p[i + j]); else fprintf(g_log, "   ");
        fprintf(g_log, " |");
        for (unsigned j = 0; j < 16 && i + j < n; j++) {
            unsigned char c = p[i + j];
            fputc((c >= 0x20 && c < 0x7f) ? c : '.', g_log);
        }
        fprintf(g_log, "|\n");
    }
}

extern "C" void __cdecl LogAddRemote(void* thisptr, unsigned seat, unsigned blobLen, char* blob, void* ret)
{
    if (!g_log) return;
    unsigned seatB = seat & 0xff;
    unsigned lenW = blobLen & 0xffff;
    fprintf(g_log, "\n[#%d] AddRemotePlayer  this=%p  seat=%u (0x%02x)  blobLen=%u  blob=%p  caller(rakion.exe)=%p\n",
            g_calls++, thisptr, seatB, seatB, lenW, (void*)blob, ret);
    // dump do blob (o CPlayerCharacter serializado) — guardado por SEH contra ponteiro inválido
    if (blob) {
        unsigned n = lenW ? (lenW < 1024 ? lenW : 1024) : 256;   // se len=0, dump 256 exploratório
        __try {
            HexDump(reinterpret_cast<unsigned char*>(blob), n);
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            fprintf(g_log, "    (blob ilegível em %p)\n", (void*)blob);
        }
    }
    fflush(g_log);
}

static __declspec(naked) void HookAddRemote()
{
    __asm {
        pushad
        pushfd
        mov  ebp, esp                 // base fixa p/ ler args (ebp restaurado pelo popad)
        push dword ptr [ebp+36]       // ret addr (caller)   -> 5o arg cdecl
        push dword ptr [ebp+48]       // blob                -> 4o
        push dword ptr [ebp+44]       // blobLen             -> 3o
        push dword ptr [ebp+40]       // seat                -> 2o
        push dword ptr [ebp+28]       // this (ecx salvo no pushad) -> 1o
        call LogAddRemote
        add  esp, 20
        popfd
        popad
        mov  eax, g_trampoline
        jmp  eax
    }
}

// Instala o detour — SÓ se a engine.dll está mapeada E o prólogo bate (senão loga e NÃO toca a memória).
// Tudo sob SEH: um DllMain que crasha = DLL_INIT_FAILED (err 1114), e nunca vemos o porquê.
static void Install()
{
    HMODULE eng = GetModuleHandleA("engine.dll");
    if (!eng) { if (g_log) { fprintf(g_log, "[hook] engine.dll AUSENTE — hook NAO instalado (processo sem a engine)\n"); fflush(g_log); } return; }

    unsigned char* p = reinterpret_cast<unsigned char*>(ADDREMOTE);
    __try {
        // valida o prólogo ANTES de patchar (build diferente / endereço errado -> aborta sem corromper)
        if (memcmp(p, ORIG_PROLOGUE, 6) != 0) {
            if (g_log) {
                fprintf(g_log, "[hook] prologo INESPERADO em 0x%08lx: %02x %02x %02x %02x %02x %02x (esperava sub esp,0x9e8) — aborta\n",
                        ADDREMOTE, p[0], p[1], p[2], p[3], p[4], p[5]);
                fflush(g_log);
            }
            return;
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        if (g_log) { fprintf(g_log, "[hook] 0x%08lx ilegível (engine.dll em base diferente?) — aborta\n", ADDREMOTE); fflush(g_log); }
        return;
    }

    if (g_log) { fprintf(g_log, "[hook] engine.dll=%p; prologo OK; instalando detour em AddRemotePlayer @0x%08lx\n", (void*)eng, ADDREMOTE); fflush(g_log); }

    g_trampoline = VirtualAlloc(nullptr, 16, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    unsigned char* tr = reinterpret_cast<unsigned char*>(g_trampoline);
    memcpy(tr, ORIG_PROLOGUE, 6);                                  // prologo conhecido (6B, sub esp,0x9e8)
    tr[6] = 0xE9;                                                  // jmp de volta p/ ADDREMOTE+6
    *reinterpret_cast<DWORD*>(tr + 7) = (ADDREMOTE + 6) - (reinterpret_cast<DWORD>(tr) + 11);

    __try {
        DWORD old;
        if (!VirtualProtect(p, 8, PAGE_EXECUTE_READWRITE, &old)) {
            if (g_log) { fprintf(g_log, "[hook] VirtualProtect FALHOU err=%lu (anti-tamper protege a engine?) — aborta\n", GetLastError()); fflush(g_log); }
            return;
        }
        p[0] = 0xE9;                                              // jmp HookAddRemote (rel32)
        *reinterpret_cast<DWORD*>(p + 1) = reinterpret_cast<DWORD>(&HookAddRemote) - (ADDREMOTE + 5);
        p[5] = 0x90;                                              // nop (completa os 6B, sem instrução parcial)
        VirtualProtect(p, 8, old, &old);
        FlushInstructionCache(GetCurrentProcess(), p, 8);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        if (g_log) { fprintf(g_log, "[hook] EXCECAO ao escrever o detour — aborta\n"); fflush(g_log); }
        return;
    }

    if (g_log) { fprintf(g_log, "[hook] INSTALADO (trampoline=%p). Aguardando o 2o player entrar no stage...\n", g_trampoline); fflush(g_log); }
}

BOOL WINAPI DllMain(HINSTANCE h, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(h);
        CreateDirectoryA("C:\\temp", nullptr);
        char path[MAX_PATH];
        sprintf(path, "C:\\temp\\addremote_hook_%lu.log", GetCurrentProcessId());   // por-PID (2 clientes não colidem)
        g_log = fopen(path, "w");
        // IDENTIDADE do processo em que a DLL caiu (p/ diagnosticar injeção no processo errado/stub)
        char exe[MAX_PATH] = {0};
        GetModuleFileNameA(nullptr, exe, MAX_PATH);
        if (g_log) { fprintf(g_log, "[dll] attach em pid=%lu  exe=%s\n", GetCurrentProcessId(), exe); fflush(g_log); }
        // espera a engine.dll aparecer (até ~20s): se o processo é um stub que só depois mapeia a engine,
        // ou está em init, 2s era curto. Loga quando aparece.
        HMODULE eng = nullptr;
        for (int i = 0; i < 400; i++) { eng = GetModuleHandleA("engine.dll"); if (eng) break; Sleep(50); }
        if (g_log) { fprintf(g_log, "[dll] engine.dll %s (apos espera)\n", eng ? "APARECEU" : "NUNCA apareceu"); fflush(g_log); }
        Install();
    }
    return TRUE;
}
