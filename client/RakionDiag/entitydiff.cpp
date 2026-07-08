// entitydiff.dll (x86) — DIAGNÓSTICO PASSIVO do muro do HIT×N (task #21).
//
// PERGUNTA: por que o HIT×N nativo conta ao acertar o HUMANO-peer mas NÃO ao acertar o BOT, sendo os
// dois criados pelo MESMO 0x4b (AddRemotePlayer)? A diferença tem de estar num campo da ENTIDADE do bot
// (team/alive/HP/template/flag) que não passa no gate do combo (AddHitCount).
//
// MÉTODO (sem patch — só leitura + um getter estável): resolve a CEntity* de CADA slot via
//   GetPlayerEntity(slot) @engine.dll 0x36121530  (static __cdecl CEntity*(long); slot 0 = humano local).
// engine.dll é a SE1 com base fixa 0x36000000 sem ASLR — offset estável entre builds (ao contrário da
// entitiesmp/gamemp). Dumpa o struct cru de cada slot ocupado em snapshots ao longo do round. Offline,
// diffo o slot do HUMANO-peer (que RECEBE HIT×N) contra o slot do BOT (que não) NO MESMO cliente: os
// bytes que diferem de forma ESTRUTURAL (não posição/HP corrente) são os campos do gate.
//
// Injetada CEDO pela launcher (QueueUserAPC no launch suspenso, antes do anti-tamper armar) — o mesmo
// caminho passivo do capture_hook que já funcionou. NÃO patcheia nenhum byte do jogo.
#include <windows.h>
#include <cstdio>
#include <cstdint>

// ---- offsets ESTÁVEIS (engine.dll SE1, base 0x36000000, sem ASLR) ----
typedef void* (__cdecl* GetPlayerEntity_t)(long slot);
static const GetPlayerEntity_t GetPlayerEntity = reinterpret_cast<GetPlayerEntity_t>(0x36121530);

// singleton de game-state (mesmo marcador do capture_hook): != 0 = já num stage com game-state inicializado.
static const DWORD GAMESTATE_READY = 0x3636F760;

static const int   SLOTS       = 20;       // 0..19 (10 por time)
static const DWORD DUMP_BYTES  = 0x2800;   // cobre o combo (+0xb44..) e a zona de flags (+0x1d8) com folga
static const int   SNAPSHOTS   = 24;       // ~1 min a 2.5s: pega parado, andando e EM COMBATE
static const DWORD SNAP_MS     = 2500;

static FILE* g_log = nullptr;
static DWORD g_t0  = 0;

static void LG(const char* fmt, ...)
{
    if (!g_log) return;
    va_list ap; va_start(ap, fmt);
    fprintf(g_log, "[+%6lums] ", GetTickCount() - g_t0);
    vfprintf(g_log, fmt, ap); fprintf(g_log, "\n"); fflush(g_log);
    va_end(ap);
}

static bool Readable(const void* p, size_t n)
{
    MEMORY_BASIC_INFORMATION mbi;
    if (!VirtualQuery(p, &mbi, sizeof(mbi))) return false;
    if (mbi.State != MEM_COMMIT) return false;
    DWORD ok = PAGE_READONLY | PAGE_READWRITE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_WRITECOPY;
    if ((mbi.Protect & ok) == 0) return false;
    // não precisa caber inteiro numa região; a dump faz page-walk com SEH. Só valida o 1º byte.
    (void)n; return true;
}

// Resolve a entidade do slot (getter estável). SEH: um slot vazio/handle morto não pode derrubar o jogo.
static void* SlotEntity(long slot)
{
    __try { return GetPlayerEntity(slot); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return nullptr; }
}

// Dump cru de `bytes` a partir de `base`, page-walk com SEH (páginas ilegíveis viram zero) — o mesmo
// padrão do dump do entitiesmp. Serve p/ o diff byte-a-byte offline.
static void DumpEntity(const char* path, const uint8_t* base, DWORD bytes)
{
    FILE* f = fopen(path, "wb");
    if (!f) return;
    static uint8_t zero[0x1000] = {0};
    for (DWORD off = 0; off < bytes; off += 0x1000)
    {
        DWORD chunk = (bytes - off < 0x1000) ? (bytes - off) : 0x1000;
        __try { fwrite(base + off, 1, chunk, f); }
        __except (EXCEPTION_EXECUTE_HANDLER) { fwrite(zero, 1, chunk, f); }
    }
    fclose(f);
}

static DWORD WINAPI DumpThread(LPVOID)
{
    { FILE* m = fopen("C:\\temp\\entdiff\\loaded.txt", "w"); if (m) { fprintf(m, "entitydiff carregado\n"); fclose(m); } }

    // espera o engine.dll mapear + o game-state inicializar (entrou num stage).
    while (GetModuleHandleA("engine.dll") == nullptr) Sleep(500);
    for (int i = 0; i < 3600; i++)   // até 30 min
    {
        DWORD ready = 0;
        __try { ready = *reinterpret_cast<DWORD*>(GAMESTATE_READY); } __except (EXCEPTION_EXECUTE_HANDLER) { ready = 0; }
        if (ready != 0) break;
        Sleep(500);
    }
    LG("game-state pronto (0x3636F760 != 0). Iniciando %d snapshots a cada %lums.", SNAPSHOTS, SNAP_MS);

    for (int snap = 0; snap < SNAPSHOTS; snap++)
    {
        char occ[SLOTS * 12] = {0}; int oc = 0;
        for (long slot = 0; slot < SLOTS; slot++)
        {
            void* ent = SlotEntity(slot);
            if (ent == nullptr || !Readable(ent, DUMP_BYTES)) continue;
            char path[96];
            sprintf(path, "C:\\temp\\entdiff\\slot%02ld_snap%02d.bin", slot, snap);
            DumpEntity(path, reinterpret_cast<const uint8_t*>(ent), DUMP_BYTES);
            oc += sprintf(occ + oc, "%ld(0x%08lx) ", slot, reinterpret_cast<DWORD>(ent));
        }
        LG("snap %02d: slots ocupados = %s", snap, oc ? occ : "(nenhum)");
        Sleep(SNAP_MS);
    }

    LG("DONE — %d snapshots dumpados em C:\\temp\\entdiff\\. Diff slot do HUMANO-peer vs slot do BOT.", SNAPSHOTS);
    FILE* d = fopen("C:\\temp\\entdiff\\done.txt", "w"); if (d) { fprintf(d, "done\n"); fclose(d); }
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE h, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(h);
        CreateDirectoryA("C:\\temp", nullptr);
        CreateDirectoryA("C:\\temp\\entdiff", nullptr);
        g_log = fopen("C:\\temp\\entdiff\\entitydiff.log", "w");
        g_t0 = GetTickCount();
        LG("entitydiff injetado; PID=%lu. Aguardando engine.dll + stage...", GetCurrentProcessId());
        CreateThread(nullptr, 0, DumpThread, nullptr, 0, nullptr);
    }
    return TRUE;
}
