// Hook EXTERNO (sem injetar DLL) de entitiesmp.dll!AddHitCount @0x35153ce0, via code-cave.
//
// POR QUE: o HIT×N (combo counter) só aparece se o AddHitCount for chamado no player LOCAL. Com o bot no
// stage o combo some p/ todos. Este hook observa, no MOMENTO do golpe, se o AddHitCount é chamado e com
// que estado — p/ cravar se a supressão é UPSTREAM (não é chamado) ou INTERNA (é chamado mas abandona).
//
// TÉCNICA (a mesma do capture_addremote.exe, que fura o anti-tamper): VirtualProtectEx+WriteProcessMemory
// (LoadLibrary é bloqueado; escrita de memória NÃO). Code-cave: detour E9 -> shellcode que salva
// (this, inflictor, ret, combo[+0xb48], group[+0xb4c]) num RING de 128 registros + contador; roda os 9
// bytes roubados do prólogo e volta p/ AddHitCount+9. Na saída, des-detoura.
//
// AddHitCount (dump desempacotado, base 0x35000000): __thiscall void(this=atacante, void* inflictor)
//   56              push esi
//   8b f1           mov esi,ecx                 ; esi=this(atacante)
//   ff 15 b4 35 2b 35  call [0x352b35b4]        ; CHECK player-local -> se 0, abandona
//   ...  combo em [esi+0xb48] (count) / [esi+0xb4c] (group)
// Uso: capture_hitcount.exe [pid]   (sem pid: 1o rakion.exe com entitiesmp). Ctrl+C p/ sair.
#include <windows.h>
#include <tlhelp32.h>
#include <cstdio>
#include <cstdint>
#include <vector>

static const DWORD ADDHITCOUNT = 0x35153ce0;
static const unsigned char ORIG9[9] = { 0x56, 0x8b, 0xf1, 0xff, 0x15, 0xb4, 0x35, 0x2b, 0x35 };
static const int STEAL = 9;

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
                if (_stricmp(me.szModule, "entitiesmp.dll") == 0) { found = pe.th32ProcessID; break; }
            } while (Module32Next(ms, &me));
            CloseHandle(ms);
        }
        if (found) break;
    } while (Process32Next(snap, &pe));
    CloseHandle(snap);
    return found;
}

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
    if (!pid) { printf("[hit] nenhum rakion.exe com entitiesmp.dll (esta no stage?)\n"); return 1; }
    printf("[hit] alvo pid=%lu\n", pid);

    HANDLE h = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION, FALSE, pid);
    if (!h) { printf("[hit] OpenProcess falhou err=%lu\n", GetLastError()); return 2; }

    // valida o prólogo (se entitiesmp ainda packed/base diferente, aborta SEM instalar — não crasha)
    unsigned char cur[9]; SIZE_T rd;
    if (!ReadProcessMemory(h, (void*)ADDHITCOUNT, cur, 9, &rd) || rd != 9) { printf("[hit] read do prologo falhou\n"); return 3; }
    if (memcmp(cur, ORIG9, 9) != 0) {
        printf("[hit] prologo inesperado em 0x%08lx: ", ADDHITCOUNT);
        for (int i = 0; i < 9; i++) printf("%02x ", cur[i]);
        printf("\n      (esperava 56 8b f1 ff 15 b4 35 2b 35 — entitiesmp desempacotou? entrou no stage?)\n");
        return 4;
    }
    printf("[hit] prologo OK (AddHitCount confirmado).\n");

    BYTE* BUF = (BYTE*)VirtualAllocEx(h, NULL, 0x1000, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    BYTE* CAVE = (BYTE*)VirtualAllocEx(h, NULL, 0x200, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!BUF || !CAVE) { printf("[hit] VirtualAllocEx falhou err=%lu\n", GetLastError()); return 5; }
    printf("[hit] BUF=%p CAVE=%p\n", BUF, CAVE);
    std::vector<uint8_t> zero(0x1000, 0);
    WriteProcessMemory(h, BUF, zero.data(), zero.size(), NULL);

    // ---- shellcode: entry de AddHitCount ([esp]=ret [esp+4]=inflictor ; ecx=this) ----
    // após pushad(0x20)+pushfd(4), ebp=esp: this=[ebp+0x1c] ret=[ebp+0x24] inflictor=[ebp+0x28]
    // RING de 128 regs de 16B em BUF+0x20; contador total em [BUF].
    Emit e;
    e.o({ 0x60 });                     // pushad
    e.o({ 0x9C });                     // pushfd
    e.o({ 0x89, 0xE5 });               // mov ebp,esp
    e.o({ 0xBF }); e.d32((uint32_t)BUF);           // mov edi,BUF
    e.o({ 0x8B, 0x07 });               // mov eax,[edi]         (count)
    e.o({ 0xFF, 0x07 });               // inc dword [edi]
    e.o({ 0x25, 0x7F, 0x00, 0x00, 0x00 });         // and eax,0x7f
    e.o({ 0xC1, 0xE0, 0x04 });         // shl eax,4            (*16)
    e.o({ 0x8D, 0xBC, 0x07, 0x20, 0x00, 0x00, 0x00 });  // lea edi,[edi+eax+0x20]  (slot do reg)
    // reg[0]=this ; reg[4]=inflictor ; reg[8]=combo count [this+0xb48] ; reg[0xC]=combo group [this+0xb4c]
    e.o({ 0x8B, 0x45, 0x1C });         // mov eax,[ebp+0x1c]   (this)
    e.o({ 0x89, 0x07 });               // mov [edi],eax
    e.o({ 0x8B, 0x45, 0x28 });         // mov eax,[ebp+0x28]   (inflictor)
    e.o({ 0x89, 0x47, 0x04 });         // mov [edi+4],eax
    e.o({ 0x8B, 0x4D, 0x1C });         // mov ecx,[ebp+0x1c]   (this)
    e.o({ 0x8B, 0x81, 0x48, 0x0B, 0x00, 0x00 });   // mov eax,[ecx+0xb48]  (combo count)
    e.o({ 0x89, 0x47, 0x08 });         // mov [edi+8],eax
    e.o({ 0x8B, 0x81, 0x4C, 0x0B, 0x00, 0x00 });   // mov eax,[ecx+0xb4c]  (combo group)
    e.o({ 0x89, 0x47, 0x0C });         // mov [edi+0xc],eax
    e.o({ 0x9D });                     // popfd
    e.o({ 0x61 });                     // popad
    for (int i = 0; i < STEAL; i++) e.o({ ORIG9[i] });   // prólogo original (9 bytes)
    e.o({ 0xE9 });                     // jmp AddHitCount+9
    uint32_t jmpAt = (uint32_t)CAVE + (uint32_t)e.b.size() - 1;
    e.d32((ADDHITCOUNT + STEAL) - (jmpAt + 5));
    if (!WriteProcessMemory(h, CAVE, e.b.data(), e.b.size(), NULL)) { printf("[hit] write CAVE falhou err=%lu\n", GetLastError()); return 6; }

    // detour: E9 rel32 em AddHitCount + NOPs até 9 bytes
    unsigned char det[9] = { 0xE9, 0, 0, 0, 0, 0x90, 0x90, 0x90, 0x90 };
    *(uint32_t*)(det + 1) = (uint32_t)CAVE - (ADDHITCOUNT + 5);
    DWORD old;
    if (!VirtualProtectEx(h, (void*)ADDHITCOUNT, 9, PAGE_EXECUTE_READWRITE, &old)) { printf("[hit] VirtualProtectEx falhou err=%lu\n", GetLastError()); return 7; }
    BOOL wok = WriteProcessMemory(h, (void*)ADDHITCOUNT, det, 9, NULL);
    VirtualProtectEx(h, (void*)ADDHITCOUNT, 9, old, &old);
    FlushInstructionCache(h, (void*)ADDHITCOUNT, 9);
    if (!wok) { printf("[hit] write detour falhou err=%lu\n", GetLastError()); return 8; }
    printf("[hit] detour INSTALADO. BATA no oponente. Ctrl+C p/ sair (des-detoura). Log: C:\\temp\\hitcount.log\n");

    FILE* log = fopen("C:\\temp\\hitcount.log", "w");
    fprintf(log, "[hit] hook AddHitCount @0x%08lx BUF=%p CAVE=%p pid=%lu\n", ADDHITCOUNT, BUF, CAVE, pid);
    fflush(log);

    // POLL do contador: cada nova chamada = uma linha (this, inflictor, combo count, group)
    DWORD last = 0;
    while (!g_stop) {
        DWORD cnt = 0;
        if (ReadProcessMemory(h, BUF, &cnt, 4, &rd) && cnt != last) {
            unsigned char ring[0x820];
            if (ReadProcessMemory(h, BUF, ring, sizeof(ring), &rd)) {
                // mostra os registros de last..cnt (ring de 128)
                for (DWORD i = last; i < cnt; i++) {
                    unsigned char* r = ring + 0x20 + (i & 0x7f) * 16;
                    DWORD thiz = *(DWORD*)r, infl = *(DWORD*)(r + 4), combo = *(DWORD*)(r + 8), grp = *(DWORD*)(r + 0xc);
                    fprintf(log, "[#%lu] AddHitCount  this(atacante)=0x%08lx  inflictor=0x%08lx  combo[+0xb48]=%ld  group[+0xb4c]=%ld\n",
                            i + 1, thiz, infl, (long)combo, (long)grp);
                }
                fflush(log);
                printf("[hit] %lu chamada(s) de AddHitCount (ver C:\\temp\\hitcount.log)\n", cnt);
            }
            last = cnt;
        }
        Sleep(20);
    }

    // des-detoura
    if (VirtualProtectEx(h, (void*)ADDHITCOUNT, 9, PAGE_EXECUTE_READWRITE, &old)) {
        WriteProcessMemory(h, (void*)ADDHITCOUNT, ORIG9, 9, NULL);
        VirtualProtectEx(h, (void*)ADDHITCOUNT, 9, old, &old);
        FlushInstructionCache(h, (void*)ADDHITCOUNT, 9);
    }
    printf("[hit] detour removido. %lu chamada(s) total. Log: C:\\temp\\hitcount.log\n", last);
    if (log) fclose(log);
    CloseHandle(h);
    return 0;
}
