// Hook EXTERNO (sem injetar DLL) de engine.dll!AddRemotePlayer @0x3610e2b0, via code-cave.
//
// POR QUE EXTERNO: o anti-tamper do cliente bloqueia LoadLibrary (LdrLoadDll hook) — a DLL de hook
// nem mapeia. Mas VirtualProtectEx/WriteProcessMemory FUNCIONAM (os patches de janela do launcher
// provam). Então instalamos um code-cave no processo do jogo por escrita de memória pura:
//   1) VirtualAllocEx um BUFFER (dados) e um CAVE (shellcode) no rakion.exe.
//   2) shellcode: ao ser chamado, salva (seat, blobLen, blob*, this, ret) + copia 0x200B do blob p/ o
//      BUFFER e incrementa um contador; roda o prólogo original (sub esp,0x9e8) e volta p/ ADDREMOTE+6.
//   3) detour: E9 em 0x3610e2b0 -> CAVE (VirtualProtectEx + WriteProcessMemory).
//   4) POLL: lê o contador; quando sobe, lê o registro + o blob (CPlayerCharacter serializado) e grava.
//   5) na saída, restaura os 6 bytes originais (des-detoura) — não deixa o cliente instável.
//
// É o INSUMO p/ sintetizar a mensagem de create-combatente server-side (docs headless-engine-re §22.7).
// Uso: capture_addremote.exe [pid]   (sem pid: acha o 1o rakion.exe com engine.dll). Ctrl+C p/ sair.
#include <windows.h>
#include <tlhelp32.h>
#include <psapi.h>
#include <cstdio>
#include <cstdint>
#include <vector>

static const DWORD ADDREMOTE = 0x3610e2b0;   // entrada real (pode estar hookada por outra DLL de diag)
// Hookamos LOGO APÓS o prólogo (0x3610e2b0: sub esp,0x9e8 = 6B), em 0x3610e2b6 (mov eax,[0x36299990] = 5B).
// Isso COEXISTE com um hook de entrada (ex.: sessprobe.dll rouba os 6 bytes de 0x3610e2b0): o trampolim dele
// roda o `sub esp` e cai em 0x3610e2b6 -> onde estamos. E funciona igual sem hook nenhum. Robusto aos dois casos.
static const DWORD HOOK_AT = 0x3610e2b6;
static const unsigned char DISPLACED[5] = { 0xA1, 0x90, 0x99, 0x29, 0x36 }; // mov eax,[0x36299990]
static const DWORD CONT = HOOK_AT + 5;       // 0x3610e2bb (continuação após a instrução deslocada)
static const DWORD ENGINE_BASE = 0x36000000;

static volatile bool g_stop = false;
static BOOL WINAPI OnCtrlC(DWORD) { g_stop = true; return TRUE; }

static void EnableDebugPriv()
{
    HANDLE tok;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &tok)) return;
    TOKEN_PRIVILEGES tp; tp.PrivilegeCount = 1; tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
    if (LookupPrivilegeValue(NULL, SE_DEBUG_NAME, &tp.Privileges[0].Luid))
        AdjustTokenPrivileges(tok, FALSE, &tp, sizeof(tp), NULL, NULL);
    CloseHandle(tok);
}

// Acha o 1o rakion.exe que tem a engine.dll carregada (o jogo real, não um stub).
static DWORD FindGamePid()
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    PROCESSENTRY32 pe; pe.dwSize = sizeof(pe);
    DWORD found = 0;
    if (Process32First(snap, &pe)) do {
        if (_stricmp(pe.szExeFile, "rakion.exe") != 0) continue;
        HANDLE ms = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pe.th32ProcessID);
        if (ms != INVALID_HANDLE_VALUE) {
            MODULEENTRY32 me; me.dwSize = sizeof(me);
            if (Module32First(ms, &me)) do {
                if (_stricmp(me.szModule, "engine.dll") == 0) { found = pe.th32ProcessID; break; }
            } while (Module32Next(ms, &me));
            CloseHandle(ms);
        }
        if (found) break;
    } while (Process32Next(snap, &pe));
    CloseHandle(snap);
    return found;
}

// Emissor de shellcode.
struct Emit {
    std::vector<uint8_t> b;
    void o(std::initializer_list<uint8_t> x) { for (auto v : x) b.push_back(v); }
    void d32(uint32_t v) { o({ (uint8_t)v, (uint8_t)(v >> 8), (uint8_t)(v >> 16), (uint8_t)(v >> 24) }); }
};

int main(int argc, char** argv)
{
    SetConsoleCtrlHandler(OnCtrlC, TRUE);
    EnableDebugPriv();

    DWORD pid = (argc > 1) ? (DWORD)atoi(argv[1]) : 0;
    for (int i = 0; !pid && i < 20; i++) { pid = FindGamePid(); if (!pid) Sleep(500); }   // retry: engine.dll pode nao ter subido ainda
    if (!pid) { printf("[cap] nenhum rakion.exe com engine.dll encontrado (o jogo esta no stage?)\n"); return 1; }
    printf("[cap] alvo pid=%lu\n", pid);

    HANDLE h = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION, FALSE, pid);
    if (!h) { printf("[cap] OpenProcess falhou err=%lu\n", GetLastError()); return 2; }

    // valida a instrução deslocada em HOOK_AT (0x3610e2b6, mov eax,[0x36299990]) — intacta com ou sem
    // hook de entrada de outra DLL (esta só rouba os 6 bytes de 0x3610e2b0). base fixa 0x36000000.
    unsigned char cur[8];
    SIZE_T rd;
    if (!ReadProcessMemory(h, (void*)HOOK_AT, cur, 5, &rd) || rd < 5) { printf("[cap] read de HOOK_AT falhou\n"); return 3; }
    if (memcmp(cur, DISPLACED, 5) != 0) {
        printf("[cap] HOOK_AT inesperado: %02x %02x %02x %02x %02x (esperava mov eax,[0x36299990]) — engine build diferente?\n",
               cur[0], cur[1], cur[2], cur[3], cur[4]);
        CloseHandle(h);
        return 4;
    }

    // aloca BUFFER (dados) e CAVE (codigo) no alvo
    BYTE* BUF = (BYTE*)VirtualAllocEx(h, NULL, 0x1000, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    BYTE* CAVE = (BYTE*)VirtualAllocEx(h, NULL, 0x200, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!BUF || !CAVE) { printf("[cap] VirtualAllocEx falhou err=%lu\n", GetLastError()); return 5; }
    printf("[cap] BUF=%p CAVE=%p\n", BUF, CAVE);

    // zera o BUFFER
    std::vector<uint8_t> zero(0x1000, 0);
    WriteProcessMemory(h, BUF, zero.data(), zero.size(), NULL);

    // ---- monta o shellcode (roda no alvo em HOOK_AT=0x3610e2b6, JÁ após `sub esp,0x9e8`) ----
    // esp = entry-0x9e8. args originais: ret=[esp+0x9e8] seat=[esp+0x9ec] blobLen=[esp+0x9f0] blob=[esp+0x9f4].
    // ecx = this (intacto). Após pushad(0x20)+pushfd(4)+mov ebp,esp -> ebp+0x24 alcança o esp de entrada.
    //   ret=ebp+0xa0c seat=ebp+0xa10 blobLen=ebp+0xa14 blob=ebp+0xa18 ; this=ecx salvo em ebp+0x1c
    Emit e;
    auto movEax = [&](uint32_t disp) { e.o({ 0x8B, 0x85 }); e.d32(disp); };   // mov eax,[ebp+disp32]
    e.o({ 0x60 });                    // pushad
    e.o({ 0x9C });                    // pushfd
    e.o({ 0x89, 0xE5 });              // mov ebp, esp
    e.o({ 0xFC });                    // cld
    e.o({ 0xBF }); e.d32((uint32_t)BUF);         // mov edi, BUF
    e.o({ 0xFF, 0x07 });              // inc dword [edi]
    movEax(0xa10); e.o({ 0x89, 0x47, 0x04 });    // seat    -> [edi+4]
    movEax(0xa14); e.o({ 0x89, 0x47, 0x08 });    // blobLen -> [edi+8]
    movEax(0xa18); e.o({ 0x89, 0x47, 0x0C });    // blob    -> [edi+0xC]
    e.o({ 0x8B, 0x45, 0x1C }); e.o({ 0x89, 0x47, 0x10 });   // this (ecx) [ebp+0x1c] -> [edi+0x10]
    movEax(0xa0c); e.o({ 0x89, 0x47, 0x14 });    // ret     -> [edi+0x14]
    // copia 0x200 bytes do blob -> BUF+0x20 (se blob != 0)
    e.o({ 0x8B, 0xB5 }); e.d32(0xa18);           // mov esi,[ebp+0xa18]  (blob)
    e.o({ 0x85, 0xF6 });              // test esi,esi
    e.o({ 0x74, 0x0A });              // jz +0x0A (pula lea+mov ecx+rep = 3+5+2)
    e.o({ 0x8D, 0x7F, 0x20 });        // lea edi,[edi+0x20]  (edi ainda = BUF)
    e.o({ 0xB9 }); e.d32(0x200);      // mov ecx,0x200
    e.o({ 0xF3, 0xA4 });              // rep movsb
    // (skip target)
    e.o({ 0x9D });                    // popfd
    e.o({ 0x61 });                    // popad
    e.o({ 0xA1, 0x90, 0x99, 0x29, 0x36 });       // instrução DESLOCADA: mov eax,[0x36299990]
    e.o({ 0xE9 });                    // jmp CONT (0x3610e2bb)
    uint32_t jmpAt = (uint32_t)CAVE + (uint32_t)e.b.size() - 1;
    e.d32(CONT - (jmpAt + 5));

    if (!WriteProcessMemory(h, CAVE, e.b.data(), e.b.size(), NULL)) { printf("[cap] write do CAVE falhou err=%lu\n", GetLastError()); return 6; }

    // ---- detour: E9 rel32 em HOOK_AT (5B exatos = tam. do mov eax) -> CAVE ----
    unsigned char det[5] = { 0xE9, 0, 0, 0, 0 };
    *(uint32_t*)(det + 1) = (uint32_t)CAVE - (HOOK_AT + 5);
    DWORD old;
    if (!VirtualProtectEx(h, (void*)HOOK_AT, 5, PAGE_EXECUTE_READWRITE, &old)) { printf("[cap] VirtualProtectEx falhou err=%lu\n", GetLastError()); return 7; }
    BOOL wok = WriteProcessMemory(h, (void*)HOOK_AT, det, 5, NULL);
    VirtualProtectEx(h, (void*)HOOK_AT, 5, old, &old);
    FlushInstructionCache(h, (void*)HOOK_AT, 5);
    if (!wok) { printf("[cap] write do detour falhou err=%lu\n", GetLastError()); return 8; }
    printf("[cap] detour INSTALADO em 0x%08lx. Poll ativo — entre no stage com o 2o player. Ctrl+C p/ sair (des-detoura).\n", HOOK_AT);

    FILE* log = fopen("C:\\temp\\addremote_capture.log", "w");
    fprintf(log, "[cap] hook externo em AddRemotePlayer (HOOK_AT 0x%08lx)  BUF=%p CAVE=%p pid=%lu\n", HOOK_AT, BUF, CAVE, pid);
    fflush(log);

    // ---- POLL do contador ----
    DWORD lastCount = 0;
    while (!g_stop) {
        DWORD cnt = 0;
        if (ReadProcessMemory(h, BUF, &cnt, 4, &rd) && cnt != lastCount) {
            unsigned char rec[0x220];
            if (ReadProcessMemory(h, BUF, rec, sizeof(rec), &rd)) {
                DWORD seat = *(DWORD*)(rec + 4), blen = *(DWORD*)(rec + 8);
                DWORD blob = *(DWORD*)(rec + 0x0C), thiz = *(DWORD*)(rec + 0x10), ret = *(DWORD*)(rec + 0x14);
                DWORD retRva = (ret >= ENGINE_BASE && ret < ENGINE_BASE + 0x400000) ? (ret - ENGINE_BASE) : 0;
                fprintf(log, "\n[#%lu] AddRemotePlayer seat=%lu (0x%02lx) blobLen=%lu blob=0x%08lx this=0x%08lx caller=0x%08lx%s\n",
                        cnt, seat & 0xff, seat & 0xff, blen & 0xffff, blob, thiz, ret,
                        retRva ? " (engine.dll+RVA 0x" : " (rakion.exe descomprimido)");
                if (retRva) fprintf(log, "     caller RVA engine.dll = 0x%06lx\n", retRva);
                unsigned n = (blen & 0xffff); if (n > 0x200) n = 0x200;
                unsigned char* p = rec + 0x20;
                for (unsigned i = 0; i < n; i += 16) {
                    fprintf(log, "     %04x: ", i);
                    for (unsigned j = 0; j < 16; j++) if (i + j < n) fprintf(log, "%02x ", p[i + j]); else fprintf(log, "   ");
                    fprintf(log, " |");
                    for (unsigned j = 0; j < 16 && i + j < n; j++) { unsigned char c = p[i + j]; fputc((c >= 0x20 && c < 0x7f) ? c : '.', log); }
                    fprintf(log, "|\n");
                }
                fflush(log);
                printf("[cap] CAPTURA #%lu: seat=%lu blobLen=%lu (ver C:\\temp\\addremote_capture.log)\n", cnt, seat & 0xff, blen & 0xffff);
            }
            lastCount = cnt;
        }
        Sleep(20);
    }

    // ---- restaura a instrução deslocada em HOOK_AT (des-detoura) ----
    if (VirtualProtectEx(h, (void*)HOOK_AT, 5, PAGE_EXECUTE_READWRITE, &old)) {
        WriteProcessMemory(h, (void*)HOOK_AT, DISPLACED, 5, NULL);
        VirtualProtectEx(h, (void*)HOOK_AT, 5, old, &old);
        FlushInstructionCache(h, (void*)HOOK_AT, 5);
    }
    printf("[cap] detour removido. %lu captura(s). Log: C:\\temp\\addremote_capture.log\n", lastCount);
    if (log) fclose(log);
    CloseHandle(h);
    return 0;
}
