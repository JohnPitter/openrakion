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
#include <cstdio>
#include <cstdint>
#include <vector>

static const DWORD ADDREMOTE = 0x3610e2b0;
static const unsigned char ORIG6[6] = { 0x81, 0xEC, 0xE8, 0x09, 0x00, 0x00 }; // sub esp,0x9e8
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

    DWORD pid = (argc > 1) ? (DWORD)atoi(argv[1]) : FindGamePid();
    if (!pid) { printf("[cap] nenhum rakion.exe com engine.dll encontrado (o jogo esta no stage?)\n"); return 1; }
    printf("[cap] alvo pid=%lu\n", pid);

    HANDLE h = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION, FALSE, pid);
    if (!h) { printf("[cap] OpenProcess falhou err=%lu\n", GetLastError()); return 2; }

    // valida o prologo de AddRemotePlayer no alvo (engine.dll base fixa 0x36000000)
    unsigned char cur[6];
    SIZE_T rd;
    if (!ReadProcessMemory(h, (void*)ADDREMOTE, cur, 6, &rd) || rd != 6) { printf("[cap] read do prologo falhou\n"); return 3; }
    if (memcmp(cur, ORIG6, 6) != 0) {
        printf("[cap] prologo inesperado: %02x %02x %02x %02x %02x %02x (esperava sub esp,0x9e8)\n",
               cur[0], cur[1], cur[2], cur[3], cur[4], cur[5]);
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

    // ---- monta o shellcode (roda no contexto do alvo; entrada = entry de AddRemotePlayer) ----
    // [esp]=ret [esp+4]=seat [esp+8]=blobLen [esp+0xC]=blob ; ecx=this
    Emit e;
    e.o({ 0x60 });                    // pushad
    e.o({ 0x9C });                    // pushfd
    e.o({ 0x89, 0xE5 });              // mov ebp, esp
    e.o({ 0xFC });                    // cld (rep movsb p/ frente)
    e.o({ 0xBF }); e.d32((uint32_t)BUF);         // mov edi, BUF
    e.o({ 0xFF, 0x07 });              // inc dword [edi]  (contador)
    e.o({ 0x8B, 0x45, 40 }); e.o({ 0x89, 0x47, 0x04 });   // seat  [ebp+40] -> [edi+4]
    e.o({ 0x8B, 0x45, 44 }); e.o({ 0x89, 0x47, 0x08 });   // blobLen [ebp+44] -> [edi+8]
    e.o({ 0x8B, 0x45, 48 }); e.o({ 0x89, 0x47, 0x0C });   // blob  [ebp+48] -> [edi+0xC]
    e.o({ 0x8B, 0x45, 28 }); e.o({ 0x89, 0x47, 0x10 });   // this  [ebp+28] -> [edi+0x10]
    e.o({ 0x8B, 0x45, 36 }); e.o({ 0x89, 0x47, 0x14 });   // ret   [ebp+36] -> [edi+0x14]
    // copia 0x200 bytes do blob -> BUF+0x20 (se blob != 0)
    e.o({ 0x8B, 0x75, 48 });          // mov esi,[ebp+48]  (blob)
    e.o({ 0x85, 0xF6 });              // test esi,esi
    e.o({ 0x74, 0x0A });              // jz +0x0A (pula lea+mov ecx+rep = 3+5+2)
    e.o({ 0x8D, 0x7F, 0x20 });        // lea edi,[edi+0x20]   (edi ainda = BUF)
    e.o({ 0xB9 }); e.d32(0x200);      // mov ecx,0x200
    e.o({ 0xF3, 0xA4 });              // rep movsb
    // (skip target)
    e.o({ 0x9D });                    // popfd
    e.o({ 0x61 });                    // popad
    e.o({ 0x81, 0xEC, 0xE8, 0x09, 0x00, 0x00 });  // sub esp,0x9e8 (prologo original)
    e.o({ 0xE9 });                    // jmp ADDREMOTE+6
    uint32_t jmpAt = (uint32_t)CAVE + (uint32_t)e.b.size() - 1;
    e.d32((ADDREMOTE + 6) - (jmpAt + 5));

    if (!WriteProcessMemory(h, CAVE, e.b.data(), e.b.size(), NULL)) { printf("[cap] write do CAVE falhou err=%lu\n", GetLastError()); return 6; }

    // ---- detour: E9 rel32 em ADDREMOTE -> CAVE (+ nop no 6o byte) ----
    unsigned char det[6] = { 0xE9, 0, 0, 0, 0, 0x90 };
    *(uint32_t*)(det + 1) = (uint32_t)CAVE - (ADDREMOTE + 5);
    DWORD old;
    if (!VirtualProtectEx(h, (void*)ADDREMOTE, 6, PAGE_EXECUTE_READWRITE, &old)) { printf("[cap] VirtualProtectEx falhou err=%lu\n", GetLastError()); return 7; }
    BOOL wok = WriteProcessMemory(h, (void*)ADDREMOTE, det, 6, NULL);
    VirtualProtectEx(h, (void*)ADDREMOTE, 6, old, &old);
    if (!wok) { printf("[cap] write do detour falhou err=%lu\n", GetLastError()); return 8; }
    printf("[cap] detour INSTALADO. Poll ativo — entre no stage com o 2o player. Ctrl+C p/ sair (des-detoura).\n");

    FILE* log = fopen("C:\\temp\\addremote_capture.log", "w");
    fprintf(log, "[cap] hook externo em AddRemotePlayer @0x%08lx  BUF=%p CAVE=%p pid=%lu\n", ADDREMOTE, BUF, CAVE, pid);
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

    // ---- restaura o prologo original (des-detoura) ----
    if (VirtualProtectEx(h, (void*)ADDREMOTE, 6, PAGE_EXECUTE_READWRITE, &old)) {
        WriteProcessMemory(h, (void*)ADDREMOTE, ORIG6, 6, NULL);
        VirtualProtectEx(h, (void*)ADDREMOTE, 6, old, &old);
        FlushInstructionCache(h, (void*)ADDREMOTE, 6);
    }
    printf("[cap] detour removido. %lu captura(s). Log: C:\\temp\\addremote_capture.log\n", lastCount);
    if (log) fclose(log);
    CloseHandle(h);
    return 0;
}
