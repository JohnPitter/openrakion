// msgfix.dll — patch client-side do render da janela F9 do messenger (rakion.exe x86, ImageBase
// 0x400000, SEM ASLR). Injetada pelo RakionLauncher no launch (ver injecao-dll-pelo-launcher: injetar
// por fora TRAVA o jogo). Offsets verificados byte-a-byte entre rakion_orig.exe (RE) e
// rakion-final/Bin/rakion.exe (cliente real). Documentacao: docs/protocol-buddy.md "Render da janela F9".
//
// PROBLEMA (cravado por RE + diagnostico runtime):
//  1) A janela do messenger e criada OCULTA no login (FUN_0047bce0 -> FUN_0040bf90); o F9
//     (FUN_0040e8e0 -> FUN_00482020 -> FUN_00489120) so faz toggle de visibilidade e reexibe os
//     widgets — NAO reconstroi o titulo/lista a partir do store. Por isso nasciam VAZIOS na abertura
//     (so o nick-change, que dispara o refresh FUN_00483600, montava).
//  2) O self-name (host+0x44, std::string) chega TRUNCADO em 2 chars ("Go") porque o campo do 0x0C
//     (@41) e fixo em 2 bytes na sintese do login.
//
// FIX (1x por sessao, no 1o SHOW):
//  a) Le o prefixo de 2 chars do self-name, acha o nome COMPLETO em AccountInfo que comeca com ele
//     ("GoHeroi") e grava de volta em host+0x44 (auto-validante).
//  b) Chama FUN_00483600(host) — o MESMO refresh do nick-change — pra montar titulo + linhas.
//
// std::string do VC7 (cravado por dump cru @host+0x44): union _Bx (buf[16]/ptr) em base+0x04,
// _Mysize em base+0x14, _Myres em base+0x18. SSO se _Myres<=15 (buf inline), senao _Ptr em base+0x04.
#include <windows.h>

static const DWORD F9_SHOW  = 0x00489120;   // FUN_00489120 toggle show/hide (fastcall: ecx=messengerHost)
static const DWORD REFRESH  = 0x00483600;   // FUN_00483600 (host vtable+0x6c): monta titulo + reconstroi linhas
static const DWORD GETNET   = 0x00471b70;   // FUN_00471b70: retorna o net singleton (eax)
static const DWORD GETACCT_THUNK = 0x004d054c;   // IAT: IScavengerWorldNet::GetAccountInfo (__thiscall)

static const int STR_BUF = 0x04, STR_SIZE = 0x14, STR_RES = 0x18;

typedef void (__fastcall *Refresh_t)(void* host);
typedef void* (*GetNet_t)();
typedef void* (__thiscall *GetAcct_t)(void* net);

static void* g_tr_show = nullptr;
static bool  g_done = false;

static void ReadStdStr(DWORD base, char* out, int cap) {
    out[0] = 0;
    __try {
        unsigned int mysize = *reinterpret_cast<unsigned int*>(base + STR_SIZE);
        unsigned int myres  = *reinterpret_cast<unsigned int*>(base + STR_RES);
        const char* s = (myres <= 15) ? reinterpret_cast<const char*>(base + STR_BUF)
                                      : *reinterpret_cast<const char**>(base + STR_BUF);
        if (!s) return;
        unsigned int n = (mysize < (unsigned)(cap - 1)) ? mysize : (unsigned)(cap - 1);
        for (unsigned int i = 0; i < n; i++) out[i] = s[i];
        out[n] = 0;
    } __except (EXCEPTION_EXECUTE_HANDLER) { out[0] = 0; }
}

static bool WriteStdStr(DWORD base, const char* name) {
    __try {
        unsigned int myres = *reinterpret_cast<unsigned int*>(base + STR_RES);
        int len = 0; while (name[len]) len++;
        char* buf;
        if (myres <= 15) { buf = reinterpret_cast<char*>(base + STR_BUF); if (len > 15) len = 15; }
        else { buf = *reinterpret_cast<char**>(base + STR_BUF); if (!buf || (unsigned)len > myres) return false; }
        for (int i = 0; i < len; i++) buf[i] = name[i];
        buf[len] = 0;
        *reinterpret_cast<unsigned int*>(base + STR_SIZE) = len;
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

static void* GetAccountInfo() {
    __try {
        void* net = reinterpret_cast<GetNet_t>(GETNET)();
        if (!net) return nullptr;
        GetAcct_t getacct = *reinterpret_cast<GetAcct_t*>(GETACCT_THUNK);
        return getacct ? getacct(net) : nullptr;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return nullptr; }
}

// Primeira string alfanumerica (len 3..15) em AccountInfo que COMECA com o prefixo e e MAIS LONGA que
// ele (o nome completo do char; o cliente so recebeu 2 chars no login). Auto-validante.
static bool FindFullNameByPrefix(DWORD acct, const char* prefix, int plen, char* out, int cap) {
    out[0] = 0;
    if (!acct || plen < 1) return false;
    __try {
        for (int off = 0; off < 0x3000; off++) {
            const char* p = reinterpret_cast<const char*>(acct + off);
            bool pref = true; for (int i = 0; i < plen; i++) if (p[i] != prefix[i]) { pref = false; break; }
            if (!pref) continue;
            int n = 0; while (n < cap - 1) { char c = p[n];
                bool alnum=(c>='0'&&c<='9')||(c>='A'&&c<='Z')||(c>='a'&&c<='z'); if(!alnum)break; out[n]=c; n++; }
            if (n > plen && n <= 15 && p[n] == 0) { out[n] = 0; return true; }
            out[0] = 0;
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {}
    return false;
}

extern "C" void __cdecl OnShow(DWORD obj) {
    __try {
        unsigned char guard = *reinterpret_cast<unsigned char*>(obj + 0x24);   // FUN_00489120 aborta se 0
        unsigned char vis   = *reinterpret_cast<unsigned char*>(obj + 0x124);  // 0 = vai SHOW
        if (guard != 0 && vis == 0 && !g_done) {
            g_done = true;
            char self[64]; ReadStdStr(obj + 0x44, self, sizeof(self));
            int plen = 0; while (self[plen]) plen++;
            char full[24];
            if (plen >= 1 && plen < 15 &&
                FindFullNameByPrefix(reinterpret_cast<DWORD>(GetAccountInfo()), self, plen, full, sizeof(full)))
                WriteStdStr(obj + 0x44, full);
            reinterpret_cast<Refresh_t>(REFRESH)(reinterpret_cast<void*>(obj));   // MESMO refresh do nick-change
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {}
}

static __declspec(naked) void HookShow() {
    __asm {
        pushad
        push ecx            // obj (fastcall)
        call OnShow
        add  esp, 4
        popad
        jmp  [g_tr_show]     // executa o prologo original relocado (6B) e continua o show
    }
}

// prologo de FUN_00489120: 56 8b f1 8a 46 24 (push esi; mov esi,ecx; mov al,[esi+0x24]) = 6B, 3 instr.
static const unsigned char P_SHOW[6] = { 0x56, 0x8b, 0xf1, 0x8a, 0x46, 0x24 };

BOOL WINAPI DllMain(HINSTANCE h, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        unsigned char* tr = reinterpret_cast<unsigned char*>(
            VirtualAlloc(nullptr, 32, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
        memcpy(tr, P_SHOW, 6);
        tr[6] = 0xE9;
        *reinterpret_cast<DWORD*>(tr + 7) = (F9_SHOW + 6) - (reinterpret_cast<DWORD>(tr) + 11);
        g_tr_show = tr;
        DWORD old;
        VirtualProtect(reinterpret_cast<void*>(F9_SHOW), 8, PAGE_EXECUTE_READWRITE, &old);
        unsigned char* p = reinterpret_cast<unsigned char*>(F9_SHOW);
        p[0] = 0xE9;
        *reinterpret_cast<DWORD*>(p + 1) = reinterpret_cast<DWORD>(&HookShow) - (F9_SHOW + 5);
        p[5] = 0x90;   // NOP pad ate a fronteira de instrucao (prologo tem 6B)
        VirtualProtect(reinterpret_cast<void*>(F9_SHOW), 8, old, &old);
        FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<void*>(F9_SHOW), 8);
    }
    return TRUE;
}
