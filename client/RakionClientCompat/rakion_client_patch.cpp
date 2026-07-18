#include <windows.h>

#include <cstdint>
#include <cstring>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

#include "bot_telemetry.h"
#include "client_patches.h"
#include "compat_log.h"

namespace
{
constexpr uintptr_t PatchAddress = 0x351533e9;
constexpr uintptr_t ContinueAddress = PatchAddress + 5;
constexpr uintptr_t RangedDamageReturnAddress = 0x3519f5ad;
constexpr uintptr_t PlayerUpdateAddress = 0x35165170;
constexpr uintptr_t PlayerUpdateContinueAddress = PlayerUpdateAddress + 8;
constexpr uintptr_t SetAliveAddress = 0x35130b70;
constexpr uintptr_t SetDeadAddress = 0x35135810;
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
constexpr const char* LifecyclePath = "C:\\temp\\bot_lifecycle.txt";
volatile LONG DesiredLifecycleSequence[MaxPlayerSeats]{};
volatile LONG DesiredDeadState[MaxPlayerSeats]{};
volatile LONG AppliedLifecycleSequence[MaxPlayerSeats]{};
volatile LONG LoggedLifecycleSequence[MaxPlayerSeats]{};
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
    std::ifstream input(LifecyclePath);
    int seat{};
    int generation{};
    unsigned long sequence{};
    int dead{};
    while (input >> seat >> generation >> sequence >> dead)
    {
        if (seat < 0 || seat >= MaxPlayerSeats || generation < 0 || sequence == 0) continue;
        InterlockedExchange(&DesiredDeadState[seat], dead == 0 ? 0 : 1);
        InterlockedExchange(&DesiredLifecycleSequence[seat], static_cast<LONG>(sequence));
    }
}

void LogAppliedLifecycles()
{
    for (int seat = 0; seat < MaxPlayerSeats; ++seat)
    {
        LONG applied = InterlockedCompareExchange(&AppliedLifecycleSequence[seat], 0, 0);
        LONG logged = InterlockedCompareExchange(&LoggedLifecycleSequence[seat], 0, 0);
        if (applied == 0 || applied == logged) continue;
        InterlockedExchange(&LoggedLifecycleSequence[seat], applied);
        LONG dead = InterlockedCompareExchange(&DesiredDeadState[seat], 0, 0);
        char message[96]{};
        _snprintf_s(message, _countof(message), _TRUNCATE,
            "lifecycle seat=%d seq=%ld state=%s aplicado", seat, applied, dead != 0 ? "dead" : "alive");
        CompatLog(message);
    }
}

void __stdcall ApplyLifecycleOnGameThread(void* player)
{
    __try
    {
        if (!player) return;
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
        if (desired == applied) return;
        using LifecycleFn = void(__thiscall*)(void*);
        auto transition = reinterpret_cast<LifecycleFn>(dead != 0 ? SetDeadAddress : SetAliveAddress);
        transition(player);
        InterlockedExchange(&AppliedLifecycleSequence[seat], desired);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
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
    CompatLog(InstallBotTelemetryHook()
        ? "ponte P2P->World para hit de bot instalada"
        : "ponte P2P->World indisponível");
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
    CompatLog("compatibilidade HIT/SHOT e lifecycle instalada");
    for (;;)
    {
        LoadLifecycleSnapshot();
        LogAppliedLifecycles();
        Sleep(10);
    }
}
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    if (!IsRakionProcess()) return TRUE;
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
