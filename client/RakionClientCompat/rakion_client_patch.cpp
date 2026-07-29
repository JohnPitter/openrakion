#include <windows.h>

#include <cstdint>
#include <cstring>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

#include "bot_telemetry.h"
#include "buddy_refresh.h"
#include "cash_store.h"
#include "client_patches.h"
#include "ui_lifecycle_patch.h"
#include "compat_log.h"
#include "headless_mode.h"
#include "headless_world_session.h"

namespace
{
constexpr uintptr_t PatchAddress = 0x351533e9;
constexpr uintptr_t ContinueAddress = PatchAddress + 5;
constexpr uintptr_t RangedDamageReturnAddress = 0x3519f5ad;
constexpr uintptr_t PlayerUpdateAddress = 0x35165170;
constexpr uintptr_t PlayerUpdateContinueAddress = PlayerUpdateAddress + 8;
constexpr uintptr_t SetAliveAddress = 0x35130b70;
constexpr uintptr_t SetDeadAddress = 0x35135810;
constexpr uintptr_t ExecDamageAnimAddress = 0x3514a6c0;
constexpr uintptr_t ExecNormalAnimAddress = 0x3513e570;
constexpr uintptr_t AddHitCountAddress = 0x35153ce0;
constexpr char GetLocalPlayerSymbol[] = "?GetLocalPlayer@CPlayer@@QAEPAV1@XZ";
// CEntity::FallDownToFloor @ engine.dll (base 0x36000000). void __thiscall(this): casta 4 raios p/ baixo
// dos cantos da collision-box, acha o chão mais alto e ajusta SÓ o Y ao piso real (mantém X/Z) via
// SetPlacement — sem tocar velocidade/eventos. É a geometry query do invariante #7 da golden capture,
// feita pela própria engine (nada de struct de raio montado à mão). Aterra o avatar-fantasma do bot.
constexpr uintptr_t FallDownToFloorAddress = 0x36124ce0;
constexpr BYTE ExpectedFallDownToFloor[] = { 0x64, 0xa1, 0x00, 0x00, 0x00, 0x00, 0x6a, 0xff };
constexpr uint32_t ReceiveDamageStackReturnOffset = 0x4d4;
constexpr BYTE Expected[] = { 0x68, 0x30, 0xa6, 0x2b, 0x35 };
constexpr BYTE ExpectedPlayerUpdate[] = { 0x6a, 0xff, 0x64, 0xa1, 0x00, 0x00, 0x00, 0x00 };
constexpr int MaxPlayerSeats = 20;
volatile LONG GroundSnapEnabled = 0;   // 1 só após verificar o prólogo de FallDownToFloor (fail-closed)
volatile LONG DesiredLifecycleSequence[MaxPlayerSeats]{};
volatile LONG DesiredDeadState[MaxPlayerSeats]{};
volatile LONG DesiredDamageSequence[MaxPlayerSeats]{};
volatile LONG DesiredDamageAttackerSeat[MaxPlayerSeats]{};
volatile LONG DesiredMovingState[MaxPlayerSeats]{};
volatile LONG DesiredLocalHitSequence[MaxPlayerSeats]{};
volatile LONG AppliedLifecycleSequence[MaxPlayerSeats]{};
volatile LONG AppliedDamageSequence[MaxPlayerSeats]{};
volatile LONG AppliedLocalHitSequence[MaxPlayerSeats]{};
volatile LONG LocalHitSequenceInitialized[MaxPlayerSeats]{};
volatile LONG AppliedMovingState[MaxPlayerSeats]{};
volatile LONG LoggedLifecycleSequence[MaxPlayerSeats]{};
volatile LONG LoggedDamageSequence[MaxPlayerSeats]{};
volatile LONG LoggedLocalHitSequence[MaxPlayerSeats]{};
volatile LONG LocalHitDiagnosticSequence{};
volatile LONG LocalHitDiagnosticStage{};
volatile LONG LocalHitDiagnosticPublishedSeat{-1};
volatile LONG LocalHitDiagnosticFieldSeat{-1};
volatile LONG LocalHitDiagnosticEntitySeat{-1};
volatile LONG LocalHitDiagnosticException{};
volatile LONG LoggedLocalHitDiagnosticKey{};
uintptr_t PlayerUpdateContinue = PlayerUpdateContinueAddress;
bool IsRakionProcess()
{
    wchar_t path[MAX_PATH]{};
    if (GetModuleFileNameW(nullptr, path, MAX_PATH) == 0) return false;
    const wchar_t* name = wcsrchr(path, L'\\');
    return _wcsicmp(name ? name + 1 : path, L"rakion.exe") == 0;
}

void Emit(std::vector<BYTE>& code, std::initializer_list<BYTE> bytes)
{
    code.insert(code.end(), bytes.begin(), bytes.end());
}

void Emit32(std::vector<BYTE>& code, uint32_t value)
{
    const auto* bytes = reinterpret_cast<const BYTE*>(&value);
    code.insert(code.end(), bytes, bytes + sizeof(value));
}

size_t ShortBranch(std::vector<BYTE>& code, BYTE opcode)
{
    code.push_back(opcode);
    code.push_back(0);
    return code.size() - 1;
}

void PatchBranch(std::vector<BYTE>& code, size_t index, size_t target)
{
    code[index] = static_cast<BYTE>(static_cast<int8_t>(target - (index + 1)));
}

void EmitJump(std::vector<BYTE>& code, uintptr_t source, uintptr_t target)
{
    code.push_back(0xe9);
    Emit32(code, static_cast<uint32_t>(target - (source + 5)));
}

void LoadLifecycleSnapshot()
{
    uint16_t localPort{};
    if (!TryGetWorldLocalPort(localPort)) return;
    char path[MAX_PATH]{};
    _snprintf_s(path, _countof(path), _TRUNCATE,
        "C:\\temp\\bot_lifecycle_%u.txt", static_cast<unsigned>(localPort));
    std::ifstream input(path);
    if (!input) return;
    LONG nextSequence[MaxPlayerSeats]{};
    LONG nextDeadState[MaxPlayerSeats]{};
    LONG nextDamageSequence[MaxPlayerSeats]{};
    LONG nextDamageAttackerSeat[MaxPlayerSeats]{};
    LONG nextMovingState[MaxPlayerSeats]{};
    LONG nextLocalHitSequence[MaxPlayerSeats]{};
    int seat{};
    int generation{};
    unsigned long sequence{};
    int dead{};
    unsigned long damageSequence{};
    int attackerSeat{};
    unsigned long attackerHitSequence{};
    int moving{};
    while (input >> seat >> generation >> sequence >> dead >> damageSequence >>
           attackerSeat >> attackerHitSequence >> moving)
    {
        if (seat < 0 || seat >= MaxPlayerSeats || generation < 0 || sequence == 0) continue;
        nextDeadState[seat] = dead == 0 ? 0 : 1;
        nextSequence[seat] = static_cast<LONG>(sequence);
        nextDamageSequence[seat] = static_cast<LONG>(damageSequence);
        nextDamageAttackerSeat[seat] = attackerSeat;
        nextMovingState[seat] = moving == 0 ? 0 : 1;
        if (attackerSeat >= 0 && attackerSeat < MaxPlayerSeats)
        {
            LONG hitSequence = static_cast<LONG>(attackerHitSequence);
            if (hitSequence > nextLocalHitSequence[attackerSeat])
                nextLocalHitSequence[attackerSeat] = hitSequence;
        }
    }
    for (int index = 0; index < MaxPlayerSeats; ++index)
    {
        if (InterlockedCompareExchange(&LocalHitSequenceInitialized[index], 1, 0) == 0)
        {
            InterlockedExchange(&AppliedLocalHitSequence[index], nextLocalHitSequence[index]);
            InterlockedExchange(&LoggedLocalHitSequence[index], nextLocalHitSequence[index]);
        }
        InterlockedExchange(&DesiredDeadState[index], nextDeadState[index]);
        InterlockedExchange(&DesiredLifecycleSequence[index], nextSequence[index]);
        InterlockedExchange(&DesiredDamageSequence[index], nextDamageSequence[index]);
        InterlockedExchange(&DesiredDamageAttackerSeat[index], nextDamageAttackerSeat[index]);
        InterlockedExchange(&DesiredMovingState[index], nextMovingState[index]);
        InterlockedExchange(&DesiredLocalHitSequence[index], nextLocalHitSequence[index]);
    }
}

void LogAppliedLifecycles()
{
    for (int seat = 0; seat < MaxPlayerSeats; ++seat)
    {
        LONG applied = InterlockedCompareExchange(&AppliedLifecycleSequence[seat], 0, 0);
        LONG logged = InterlockedCompareExchange(&LoggedLifecycleSequence[seat], 0, 0);
        char message[96]{};
        if (applied != 0 && applied != logged)
        {
            InterlockedExchange(&LoggedLifecycleSequence[seat], applied);
            LONG dead = InterlockedCompareExchange(&DesiredDeadState[seat], 0, 0);
            _snprintf_s(message, _countof(message), _TRUNCATE,
                "lifecycle seat=%d seq=%ld state=%s aplicado",
                seat, applied, dead != 0 ? "dead" : "alive");
            CompatLog(message);
        }

        LONG damage = InterlockedCompareExchange(&AppliedDamageSequence[seat], 0, 0);
        LONG loggedDamage = InterlockedCompareExchange(&LoggedDamageSequence[seat], 0, 0);
        if (damage != 0 && damage != loggedDamage)
        {
            InterlockedExchange(&LoggedDamageSequence[seat], damage);
            _snprintf_s(message, _countof(message), _TRUNCATE,
                "reacao de dano seat=%d seq=%ld aplicada", seat, damage);
            CompatLog(message);
        }

        LONG hit = InterlockedCompareExchange(&AppliedLocalHitSequence[seat], 0, 0);
        LONG loggedHit = InterlockedCompareExchange(&LoggedLocalHitSequence[seat], 0, 0);
        if (hit != 0 && hit != loggedHit)
        {
            InterlockedExchange(&LoggedLocalHitSequence[seat], hit);
            _snprintf_s(message, _countof(message), _TRUNCATE,
                "HIT local seat=%d seq=%ld aplicado", seat, hit);
            CompatLog(message);
        }
    }
}

void LogLocalHitDiagnostic()
{
    LONG sequence = InterlockedCompareExchange(&LocalHitDiagnosticSequence, 0, 0);
    LONG stage = InterlockedCompareExchange(&LocalHitDiagnosticStage, 0, 0);
    if (sequence == 0 || stage == 0) return;

    LONG key = sequence * 16 + stage;
    if (InterlockedExchange(&LoggedLocalHitDiagnosticKey, key) == key) return;

    char message[192]{};
    _snprintf_s(message, _countof(message), _TRUNCATE,
        "HIT bridge seq=%ld publishedSeat=%ld fieldSeat=%ld entitySeat=%ld "
        "stage=%ld exception=0x%08lX",
        sequence,
        InterlockedCompareExchange(&LocalHitDiagnosticPublishedSeat, 0, 0),
        InterlockedCompareExchange(&LocalHitDiagnosticFieldSeat, 0, 0),
        InterlockedCompareExchange(&LocalHitDiagnosticEntitySeat, 0, 0),
        stage,
        InterlockedCompareExchange(&LocalHitDiagnosticException, 0, 0));
    CompatLog(message);
}

void ApplyLocalHit(void* contextPlayer)
{
    LONG publishedSequence{};
    int publishedSeat = -1;
    for (int seat = 0; seat < MaxPlayerSeats; ++seat)
    {
        LONG sequence = InterlockedCompareExchange(&DesiredLocalHitSequence[seat], 0, 0);
        LONG applied = InterlockedCompareExchange(&AppliedLocalHitSequence[seat], 0, 0);
        if (sequence == applied) continue;
        if (sequence <= publishedSequence) continue;
        publishedSequence = sequence;
        publishedSeat = seat;
    }
    if (publishedSequence == 0) return;

    InterlockedExchange(&LocalHitDiagnosticPublishedSeat, publishedSeat);
    InterlockedExchange(&LocalHitDiagnosticSequence, publishedSequence);
    InterlockedExchange(&LocalHitDiagnosticException, 0);
    InterlockedExchange(&LocalHitDiagnosticStage, 1);

    HMODULE entities = GetModuleHandleW(L"entitiesmp.dll");
    using LocalPlayerFn = void*(__thiscall*)(void*);
    auto localPlayer = entities
        ? reinterpret_cast<LocalPlayerFn>(
            GetProcAddress(entities, GetLocalPlayerSymbol))
        : nullptr;
    if (!localPlayer)
    {
        InterlockedExchange(&LocalHitDiagnosticStage, 2);
        return;
    }
    InterlockedExchange(&LocalHitDiagnosticStage, 3);

    void* player = localPlayer(contextPlayer);
    if (!player)
    {
        InterlockedExchange(&LocalHitDiagnosticStage, 4);
        return;
    }

    int entitySeat = *reinterpret_cast<BYTE*>(static_cast<BYTE*>(player) + 0x264);
    InterlockedExchange(&LocalHitDiagnosticEntitySeat, entitySeat);
    BYTE fieldSeat{};
    if (!TryGetCurrentFieldSeat(fieldSeat))
    {
        InterlockedExchange(&LocalHitDiagnosticStage, 5);
        return;
    }
    InterlockedExchange(&LocalHitDiagnosticFieldSeat, fieldSeat);

    LONG desired = InterlockedCompareExchange(&DesiredLocalHitSequence[fieldSeat], 0, 0);
    LONG applied = InterlockedCompareExchange(&AppliedLocalHitSequence[fieldSeat], 0, 0);
    if (desired == 0)
    {
        InterlockedExchange(&LocalHitDiagnosticStage, 6);
        return;
    }
    if (desired == applied) return;

    using AddHitCountFn = void(__thiscall*)(void*, int);
    int count = desired > applied ? desired - applied : 1;
    if (count > 8) count = 8;
    InterlockedExchange(&LocalHitDiagnosticStage, 7);
    for (int index = 0; index < count; ++index)
        reinterpret_cast<AddHitCountFn>(AddHitCountAddress)(player, 1);
    InterlockedExchange(&AppliedLocalHitSequence[fieldSeat], desired);
    InterlockedExchange(&LocalHitDiagnosticStage, 9);
}

void __stdcall ApplyLifecycleOnGameThread(void* player)
{
    __try
    {
        if (!player) return;
        ApplyLocalHit(player);

        int seat = *reinterpret_cast<BYTE*>(static_cast<BYTE*>(player) + 0x264);
        if (seat < 0 || seat >= MaxPlayerSeats) return;

        LONG desired = InterlockedCompareExchange(&DesiredLifecycleSequence[seat], 0, 0);
        if (desired == 0) return;   // seat sem lifecycle publicado = não é um bot: não tocar
        LONG dead = InterlockedCompareExchange(&DesiredDeadState[seat], 0, 0);

        // GROUND-SNAP por-frame do bot (invariante #7 da golden capture): o avatar do bot é entidade
        // dirigida-por-rede, sem física local, então flutuava sobre o mapa. FallDownToFloor consulta a
        // geometria do CWorld (4 raios p/ baixo) e ajusta SÓ o Y ao chão real — mantém o X/Z que a rede
        // já pôs. Só p/ BOTS (guard desired!=0) e só VIVOS (o morto está na anim de queda). Roda toda frame
        // do update do player, DEPOIS da rede aplicar a posição. NUNCA em humano real (não tem lifecycle
        // publicado) → não quebra pulos deles. Fail-closed se a build de engine.dll não casou o prólogo.
        if (dead == 0 && InterlockedCompareExchange(&GroundSnapEnabled, 0, 0) != 0)
        {
            using GroundFn = void(__thiscall*)(void*);
            reinterpret_cast<GroundFn>(FallDownToFloorAddress)(player);
        }

        LONG applied = InterlockedCompareExchange(&AppliedLifecycleSequence[seat], 0, 0);
        if (desired != applied)
        {
            using LifecycleFn = void(__thiscall*)(void*);
            auto transition = reinterpret_cast<LifecycleFn>(dead != 0 ? SetDeadAddress : SetAliveAddress);
            transition(player);
            InterlockedExchange(&AppliedLifecycleSequence[seat], desired);
        }

        LONG desiredMoving = InterlockedCompareExchange(&DesiredMovingState[seat], 0, 0);
        LONG appliedMoving = InterlockedCompareExchange(&AppliedMovingState[seat], 0, 0);
        if (dead == 0 && desiredMoving != appliedMoving)
        {
            using NormalAnimFn = void(__thiscall*)(void*, long);
            reinterpret_cast<NormalAnimFn>(ExecNormalAnimAddress)(
                player, desiredMoving != 0 ? 4 : 1);
            InterlockedExchange(&AppliedMovingState[seat], desiredMoving);
        }

        LONG desiredDamage = InterlockedCompareExchange(&DesiredDamageSequence[seat], 0, 0);
        LONG appliedDamage = InterlockedCompareExchange(&AppliedDamageSequence[seat], 0, 0);
        if (dead == 0 && desiredDamage != 0 && desiredDamage != appliedDamage)
        {
            using DamageAnimFn = void(__thiscall*)(void*, long, long, int);
            int attackerSeat = InterlockedCompareExchange(
                &DesiredDamageAttackerSeat[seat], 0, 0);
            reinterpret_cast<DamageAnimFn>(ExecDamageAnimAddress)(
                player, 0x0f, 0x07, attackerSeat);
            InterlockedExchange(&AppliedDamageSequence[seat], desiredDamage);
        }
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        InterlockedExchange(&LocalHitDiagnosticException, GetExceptionCode());
        InterlockedExchange(&LocalHitDiagnosticStage, 8);
    }
}

__declspec(naked) void PlayerUpdateHook()
{
    __asm
    {
        pushfd
        pushad
        push ecx
        call ApplyLifecycleOnGameThread
        popad
        popfd
        push -1
        mov eax, fs:[0]
        jmp dword ptr [PlayerUpdateContinue]
    }
}

bool InstallPlayerUpdateHook()
{
    auto* patch = reinterpret_cast<BYTE*>(PlayerUpdateAddress);
    if (std::memcmp(patch, ExpectedPlayerUpdate, sizeof(ExpectedPlayerUpdate)) != 0) return false;

    DWORD oldProtection{};
    if (!VirtualProtect(patch, sizeof(ExpectedPlayerUpdate), PAGE_EXECUTE_READWRITE, &oldProtection)) return false;
    std::vector<BYTE> detour;
    EmitJump(detour, PlayerUpdateAddress, reinterpret_cast<uintptr_t>(&PlayerUpdateHook));
    std::memcpy(patch, detour.data(), detour.size());
    std::memset(patch + detour.size(), 0x90, sizeof(ExpectedPlayerUpdate) - detour.size());
    VirtualProtect(patch, sizeof(ExpectedPlayerUpdate), oldProtection, &oldProtection);
    FlushInstructionCache(GetCurrentProcess(), patch, sizeof(ExpectedPlayerUpdate));
    return true;
}

std::vector<BYTE> BuildCode(uintptr_t cave)
{
    std::vector<BYTE> code;
    code.reserve(128);
    Emit(code, { 0x9c, 0x60 });
    Emit(code, { 0x0f, 0xb6, 0x9e, 0x64, 0x02, 0x00, 0x00, 0x83, 0xfb, 0x14 });
    const auto invalidSeat = ShortBranch(code, 0x73);
    Emit(code, { 0x8b, 0x0d, 0x60, 0xf2, 0x36, 0x36, 0x85, 0xc9 });
    const auto noGame = ShortBranch(code, 0x74);
    Emit(code, { 0x8b, 0x01, 0xff, 0x50, 0x08, 0x69, 0xdb, 0x78, 0x03, 0x00, 0x00 });
    Emit(code, { 0x0f, 0xb7, 0x94, 0x18, 0xec, 0x01, 0x00, 0x00 });
    Emit(code, { 0x66, 0x81, 0xfa, 0x9f, 0x04 });
    const auto firstBotPort = ShortBranch(code, 0x74);
    Emit(code, { 0x66, 0x81, 0xfa, 0x9f, 0x05 });
    const auto otherPort = ShortBranch(code, 0x75);
    const auto botEndpoint = code.size();
    Emit(code, { 0xa1, 0x30, 0x36, 0x2b, 0x35, 0xff, 0xd0, 0x3b, 0xe8 });
    const auto remoteAttacker = ShortBranch(code, 0x75);
    Emit(code, { 0x8b, 0x84, 0x24 });
    Emit32(code, ReceiveDamageStackReturnOffset);
    code.push_back(0x3d);
    Emit32(code, RangedDamageReturnAddress);
    Emit(code, { 0x75, 0x04, 0x6a, 0x0a, 0xeb, 0x02, 0x6a, 0x01, 0x8b, 0xcd });
    Emit(code, { 0xb8, 0xe0, 0x3c, 0x15, 0x35, 0xff, 0xd0 });
    const auto cleanup = code.size();
    PatchBranch(code, invalidSeat, cleanup);
    PatchBranch(code, noGame, cleanup);
    PatchBranch(code, firstBotPort, botEndpoint);
    PatchBranch(code, otherPort, cleanup);
    PatchBranch(code, remoteAttacker, cleanup);
    Emit(code, { 0x61, 0x9d });
    code.insert(code.end(), std::begin(Expected), std::end(Expected));
    EmitJump(code, cave + code.size(), ContinueAddress);
    return code;
}

DWORD WINAPI InstallCompatibility(void*)
{
    if (!IsRakionProcess()) return 0;
    ApplyCharacterUiLifecycleFix();
    CompatLog(InstallBotTelemetryHook()
        ? "telemetria P2P->World de movimento e hit real instalada"
        : "telemetria P2P->World indisponível");
    CompatLog(InstallBuddyRefreshHooks()
        ? "sincronizacao inicial do Messenger instalada"
        : "sincronizacao do Messenger indisponivel para esta build");
    CompatLog(InstallCashStoreRedirectHook()
        ? "redirect e botao nativo de recarga instalados"
        : "integracao de recarga indisponivel para esta build");
    PatchKeyHook();
    auto* patch = reinterpret_cast<BYTE*>(PatchAddress);
    for (int attempt = 0; attempt < 1200; ++attempt)
    {
        if (std::memcmp(patch, Expected, sizeof(Expected)) == 0) break;
        if (*patch == 0xe9) return 0;
        if (attempt == 1199) { CompatLog("entitiesmp.dll incompatível ou não carregada"); return 1; }
        Sleep(100);
    }

    auto* cave = static_cast<BYTE*>(VirtualAlloc(nullptr, 0x1000, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (!cave) { CompatLog("VirtualAlloc falhou"); return 2; }
    const auto code = BuildCode(reinterpret_cast<uintptr_t>(cave));
    std::memcpy(cave, code.data(), code.size());

    DWORD oldProtection{};
    if (!VirtualProtect(patch, sizeof(Expected), PAGE_EXECUTE_READWRITE, &oldProtection))
    {
        CompatLog("VirtualProtect falhou");
        return 3;
    }
    std::vector<BYTE> detour;
    EmitJump(detour, PatchAddress, reinterpret_cast<uintptr_t>(cave));
    std::memcpy(patch, detour.data(), detour.size());
    VirtualProtect(patch, sizeof(Expected), oldProtection, &oldProtection);
    FlushInstructionCache(GetCurrentProcess(), patch, sizeof(Expected));
    if (!InstallPlayerUpdateHook())
    {
        CompatLog("hook player-like de lifecycle falhou");
        return 4;
    }
    LoadLifecycleSnapshot();
    // Ground-snap: só habilita se a build de engine.dll casar o prólogo de FallDownToFloor (fail-closed —
    // build incompatível não faz o snap, sem patchar endereço desconhecido). Ver critério de não-regressão.
    if (std::memcmp(reinterpret_cast<BYTE*>(FallDownToFloorAddress), ExpectedFallDownToFloor,
                    sizeof(ExpectedFallDownToFloor)) == 0)
    {
        InterlockedExchange(&GroundSnapEnabled, 1);
        CompatLog("ground-snap do bot habilitado (FallDownToFloor verificado)");
    }
    else
    {
        CompatLog("ground-snap DESABILITADO: engine.dll nao casou o prologo de FallDownToFloor");
    }
    CompatLog("compatibilidade HIT/SHOT, telemetria de ataque e lifecycle instalada");
    for (;;)
    {
        LoadLifecycleSnapshot();
        PollHeadlessEngineState();
        PollHeadlessWorldSession();
        LogAppliedLifecycles();
        LogLocalHitDiagnostic();
        Sleep(10);
    }
}
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    if (!IsRakionProcess()) return TRUE;
    if (!ConfigureHeadlessEngine()) return FALSE;
    if (!ApplyFinalClientPatches()) return FALSE;
    ApplyLauncherPatches();
    CompatLog(LoadServerAddress()
        ? "server.host carregado"
        : "server.host inválido ou ausente");
    CompatLog(InstallInitialServerRedirect()
        ? "redirect TCP inicial instalado"
        : "redirect TCP inicial indisponível");
    DisableThreadLibraryCalls(instance);
    HANDLE thread = CreateThread(nullptr, 0, InstallCompatibility, nullptr, 0, nullptr);
    if (thread) CloseHandle(thread);
    return TRUE;
}
