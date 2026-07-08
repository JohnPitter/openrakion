// Hook EXTERNO da função de HIT @entitiesmp.dll 0x350d69e0 (único caller de AddHitCount; candidata a
// ReceiveDamage/hit-handler, 5 args + SEH). Observa, com o bot no stage, SE ela é chamada quando você bate
// e em QUE gate ela desvia do bloco do combo. Code-cave (WriteProcessMemory) — não injeta DLL.
//
// Prólogo (dump desempacotado): 64 a1 00 00 00 00  (mov eax,fs:0) — 6 bytes, position-independent.
// this=ecx (esi). Gates no topo: [esi+0x638], [esi+0x664], [esi+0x394]. Args (ret 0x14 = 5 dwords) em
// [esp+4..0x14] na entrada. Captura por chamada: this, arg1..arg5, +0x638, +0x664, +0x394.
#include <windows.h>
#include <tlhelp32.h>
#include <cstdio>
#include <cstdint>
#include <vector>

static const DWORD FN = 0x350d69e0;
static const unsigned char ORIG[6] = { 0x64, 0xa1, 0x00, 0x00, 0x00, 0x00 };
static const int STEAL = 6;

static volatile bool g_stop = false;
static BOOL WINAPI OnCtrlC(DWORD) { g_stop = true; return TRUE; }
static void EnableDebugPriv() {
    HANDLE t; if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &t)) return;
    TOKEN_PRIVILEGES tp; tp.PrivilegeCount = 1; tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
    if (LookupPrivilegeValue(NULL, SE_DEBUG_NAME, &tp.Privileges[0].Luid)) AdjustTokenPrivileges(t, FALSE, &tp, sizeof(tp), NULL, NULL);
    CloseHandle(t);
}
static DWORD FindGamePid() {
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    PROCESSENTRY32 pe; pe.dwSize = sizeof(pe); DWORD found = 0;
    if (Process32First(snap, &pe)) do {
        if (_stricmp(pe.szExeFile, "rakion.exe") != 0) continue;
        HANDLE ms = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pe.th32ProcessID);
        if (ms != INVALID_HANDLE_VALUE) { MODULEENTRY32 me; me.dwSize = sizeof(me);
            if (Module32First(ms, &me)) do { if (_stricmp(me.szModule, "entitiesmp.dll") == 0) { found = pe.th32ProcessID; break; } } while (Module32Next(ms, &me));
            CloseHandle(ms); }
        if (found) break;
    } while (Process32Next(snap, &pe));
    CloseHandle(snap); return found;
}
struct Emit { std::vector<uint8_t> b;
    void o(std::initializer_list<uint8_t> x) { for (auto v : x) b.push_back(v); }
    void d32(uint32_t v) { o({ (uint8_t)v, (uint8_t)(v >> 8), (uint8_t)(v >> 16), (uint8_t)(v >> 24) }); } };

int main(int argc, char** argv) {
    SetConsoleCtrlHandler(OnCtrlC, TRUE); EnableDebugPriv();
    DWORD pid = (argc > 1) ? (DWORD)atoi(argv[1]) : FindGamePid();
    if (!pid) { printf("[gate] nenhum rakion.exe com entitiesmp.dll (no stage?)\n"); return 1; }
    printf("[gate] alvo pid=%lu\n", pid);
    HANDLE h = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION, FALSE, pid);
    if (!h) { printf("[gate] OpenProcess err=%lu\n", GetLastError()); return 2; }

    unsigned char cur[6]; SIZE_T rd;
    if (!ReadProcessMemory(h, (void*)FN, cur, 6, &rd) || rd != 6) { printf("[gate] read prologo falhou\n"); return 3; }
    if (memcmp(cur, ORIG, 6) != 0) {
        printf("[gate] prologo inesperado: "); for (int i = 0; i < 6; i++) printf("%02x ", cur[i]);
        printf("(esperava 64 a1 00 00 00 00 — desempacotou/no stage?)\n"); return 4;
    }
    printf("[gate] prologo OK (fn 0x%08lx confirmada).\n", FN);

    BYTE* BUF = (BYTE*)VirtualAllocEx(h, NULL, 0x2000, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    BYTE* CAVE = (BYTE*)VirtualAllocEx(h, NULL, 0x200, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!BUF || !CAVE) { printf("[gate] VirtualAllocEx err=%lu\n", GetLastError()); return 5; }
    std::vector<uint8_t> zero(0x2000, 0); WriteProcessMemory(h, BUF, zero.data(), zero.size(), NULL);

    // shellcode: pushad+pushfd; ebp=esp. this=[ebp+0x1c]; ret=[ebp+0x24]; args=[ebp+0x28..0x38].
    // RING 128 x 0x24 bytes em BUF+0x40: this, a1..a5, [this+0x638], [this+0x664], [this+0x394].
    Emit e;
    e.o({ 0x60, 0x9C, 0x89, 0xE5 });                // pushad; pushfd; mov ebp,esp
    e.o({ 0xBF }); e.d32((uint32_t)BUF);            // mov edi,BUF
    e.o({ 0x8B, 0x07 });                            // mov eax,[edi]  (count)
    e.o({ 0xFF, 0x07 });                            // inc dword [edi]
    e.o({ 0x25, 0x7F, 0x00, 0x00, 0x00 });          // and eax,0x7f
    e.o({ 0x6B, 0xC0, 0x24 });                      // imul eax,eax,0x24  (record size)
    e.o({ 0x8D, 0xBC, 0x07, 0x40, 0x00, 0x00, 0x00 });  // lea edi,[edi+eax+0x40]
    auto cp = [&](uint8_t src, uint8_t dst){ e.o({ 0x8B, 0x45, src }); e.o({ 0x89, 0x47, dst }); };  // [ebp+src]->[edi+dst]
    cp(0x1c, 0x00);   // this
    cp(0x28, 0x04);   // arg1
    cp(0x2c, 0x08);   // arg2
    cp(0x30, 0x0c);   // arg3
    cp(0x34, 0x10);   // arg4
    cp(0x38, 0x14);   // arg5
    e.o({ 0x8B, 0x4D, 0x1C });                      // mov ecx,[ebp+0x1c]   (this)
    e.o({ 0x8B, 0x81, 0x38, 0x06, 0x00, 0x00 }); e.o({ 0x89, 0x47, 0x18 });  // [ecx+0x638] -> +0x18
    e.o({ 0x8B, 0x81, 0x64, 0x06, 0x00, 0x00 }); e.o({ 0x89, 0x47, 0x1C });  // [ecx+0x664] -> +0x1c
    e.o({ 0x8B, 0x81, 0x94, 0x03, 0x00, 0x00 }); e.o({ 0x89, 0x47, 0x20 });  // [ecx+0x394] -> +0x20
    e.o({ 0x9D, 0x61 });                            // popfd; popad
    for (int i = 0; i < STEAL; i++) e.o({ ORIG[i] });   // prólogo original (mov eax,fs:0)
    e.o({ 0xE9 });                                  // jmp FN+6
    uint32_t jmpAt = (uint32_t)CAVE + (uint32_t)e.b.size() - 1;
    e.d32((FN + STEAL) - (jmpAt + 5));
    if (!WriteProcessMemory(h, CAVE, e.b.data(), e.b.size(), NULL)) { printf("[gate] write CAVE err=%lu\n", GetLastError()); return 6; }

    unsigned char det[6] = { 0xE9, 0, 0, 0, 0, 0x90 };
    *(uint32_t*)(det + 1) = (uint32_t)CAVE - (FN + 5);
    DWORD old;
    if (!VirtualProtectEx(h, (void*)FN, 6, PAGE_EXECUTE_READWRITE, &old)) { printf("[gate] VProtEx err=%lu\n", GetLastError()); return 7; }
    BOOL wok = WriteProcessMemory(h, (void*)FN, det, 6, NULL);
    VirtualProtectEx(h, (void*)FN, 6, old, &old); FlushInstructionCache(h, (void*)FN, 6);
    if (!wok) { printf("[gate] write detour err=%lu\n", GetLastError()); return 8; }
    printf("[gate] detour INSTALADO. BATA no oponente. Ctrl+C p/ sair. Log: C:\\temp\\hitgate.log\n");

    FILE* log = fopen("C:\\temp\\hitgate.log", "a");
    fprintf(log, "\n===== RUN pid=%lu @tick %lu — fn 0x%08lx =====\n", pid, GetTickCount(), FN);
    fflush(log);
    DWORD last = 0;
    while (!g_stop) {
        DWORD cnt = 0;
        if (ReadProcessMemory(h, BUF, &cnt, 4, &rd) && cnt != last) {
            unsigned char ring[0x1240];
            if (ReadProcessMemory(h, BUF, ring, sizeof(ring), &rd)) {
                for (DWORD i = last; i < cnt; i++) {
                    DWORD* d = (DWORD*)(ring + 0x40 + (i & 0x7f) * 0x24);
                    fprintf(log, "[#%lu] this=0x%08lx args=[%08lx %08lx %08lx %08lx %08lx] +0x638=%ld +0x664=%ld +0x394=0x%08lx\n",
                            i + 1, d[0], d[1], d[2], d[3], d[4], d[5], (long)d[6], (long)d[7], d[8]);
                }
                fflush(log);
                printf("[gate] %lu chamada(s) da fn de hit (ver C:\\temp\\hitgate.log)\n", cnt);
            }
            last = cnt;
        }
        Sleep(20);
    }
    if (VirtualProtectEx(h, (void*)FN, 6, PAGE_EXECUTE_READWRITE, &old)) {
        WriteProcessMemory(h, (void*)FN, ORIG, 6, NULL);
        VirtualProtectEx(h, (void*)FN, 6, old, &old); FlushInstructionCache(h, (void*)FN, 6);
    }
    printf("[gate] detour removido. %lu chamada(s). Log: C:\\temp\\hitgate.log\n", last);
    if (log) fclose(log); CloseHandle(h); return 0;
}
