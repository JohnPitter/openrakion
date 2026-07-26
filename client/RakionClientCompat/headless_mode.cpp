#include <windows.h>

#include <array>
#include <cstdio>
#include <cstdint>
#include <cstring>

#include "compat_log.h"
#include "headless_bot_driver.h"
#include "headless_mode.h"

namespace
{
constexpr char HeadlessVariable[] = "OPENRAKION_HEADLESS";
constexpr char DedicatedServerSymbol[] = "?_bDedicatedServer@@3HA";
constexpr char NetworkSymbol[] = "?_pNetwork@@3PAVCNetworkLibrary@@A";
constexpr char LocalPlayerCountSymbol[] =
    "?GetLocalPlayerCount@CNetworkLibrary@@QAEJXZ";
constexpr char NetworkMainLoopSymbol[] =
    "?MainLoop@CNetworkLibrary@@QAEXXZ";
constexpr char InputSymbol[] = "?_pInput@@3PAVCInput@@A";
constexpr char IsInputEnabledSymbol[] =
    "?IsInputEnabled@CInput@@QBEHXZ";
constexpr char DisableInputSymbol[] = "?DisableInput@CInput@@QAEXXZ";
constexpr char ClearInputSymbol[] = "?ClearInput@CInput@@QAEXXZ";
constexpr uintptr_t FieldRosterUiTailRva = 0x7a4fc;
constexpr uintptr_t FieldRosterEpilogueRva = 0x7a57e;
constexpr uintptr_t FieldCreateUiTailRva = 0x7d080;
constexpr uintptr_t FieldCreateContinuationRva = 0x7d105;
constexpr uintptr_t PlayGameUiPrimaryRva = 0x60ef1;
constexpr uintptr_t PlayGameUiControlsRva = 0x60ef8;
constexpr uintptr_t PlayGameUiSecondaryRva = 0x60eff;
constexpr uintptr_t BattleHudRedrawRva = 0x5e2a0;
constexpr std::array<BYTE, 6> ExpectedFieldRosterUiTail{
    0x8b, 0x0d, 0xd0, 0xee, 0x4f, 0x00
};
constexpr std::array<BYTE, 6> ExpectedFieldCreateUiTail{
    0x8b, 0x15, 0xd0, 0xee, 0x4f, 0x00
};
constexpr std::array<BYTE, 5> ExpectedPlayGameUiPrimary{
    0xe8, 0x4a, 0xaf, 0xff, 0xff
};
constexpr std::array<BYTE, 5> ExpectedPlayGameUiControls{
    0xe8, 0x03, 0xf2, 0xff, 0xff
};
constexpr std::array<BYTE, 5> ExpectedPlayGameUiSecondary{
    0xe8, 0x5c, 0xb6, 0xff, 0xff
};
constexpr std::array<BYTE, 5> NoOperationCall{
    0x90, 0x90, 0x90, 0x90, 0x90
};
constexpr std::array<BYTE, 7> ExpectedBattleHudRedraw{
    0x83, 0xec, 0x14, 0x56, 0x57, 0x8b, 0xf1
};
volatile LONG HeadlessActive{};
volatile LONG LastLocalPlayerCount{-1};
volatile LONG FieldRosterUiBypassInstalled{};
volatile LONG HeadlessInputSuppressed{};
volatile LONG HeadlessExceptionCount{};

bool IsHeadlessRequested()
{
    char value[8]{};
    const DWORD length = GetEnvironmentVariableA(
        HeadlessVariable, value, static_cast<DWORD>(sizeof(value)));
    return length == 1 && value[0] == '1';
}

LONG CALLBACK TraceHeadlessException(EXCEPTION_POINTERS* exception)
{
    auto* engine = reinterpret_cast<BYTE*>(GetModuleHandleW(L"engine.dll"));
    if (!exception || !exception->ExceptionRecord || !exception->ContextRecord ||
        exception->ExceptionRecord->ExceptionCode != EXCEPTION_ACCESS_VIOLATION ||
        !engine ||
        exception->ExceptionRecord->ExceptionAddress != engine + 0x16e7 ||
        InterlockedIncrement(&HeadlessExceptionCount) > 3)
        return EXCEPTION_CONTINUE_SEARCH;

    uintptr_t returns[8]{};
    __try
    {
        uintptr_t frame = exception->ContextRecord->Ebp;
        for (size_t index = 0; index < _countof(returns); ++index)
        {
            if (frame == 0 || (frame & 3) != 0) break;
            const auto* values = reinterpret_cast<const uintptr_t*>(frame);
            returns[index] = values[1];
            const uintptr_t next = values[0];
            if (next <= frame) break;
            frame = next;
        }
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
    }

    char message[420]{};
    _snprintf_s(message, _countof(message), _TRUNCATE,
        "headless AV trace address=%p access=%p ebp=%08lX esp=%08lX "
        "returns=%08IX,%08IX,%08IX,%08IX,%08IX,%08IX,%08IX,%08IX",
        exception->ExceptionRecord->ExceptionAddress,
        reinterpret_cast<void*>(
            exception->ExceptionRecord->ExceptionInformation[1]),
        exception->ContextRecord->Ebp, exception->ContextRecord->Esp,
        returns[0], returns[1], returns[2], returns[3],
        returns[4], returns[5], returns[6], returns[7]);
    CompatLog(message);
    return EXCEPTION_CONTINUE_SEARCH;
}

template<size_t Length>
bool InstallRelativeJump(
    BYTE* image, uintptr_t sourceRva, uintptr_t targetRva,
    const std::array<BYTE, Length>& expected)
{
    static_assert(Length >= 5);
    BYTE* source = image + sourceRva;
    if (memcmp(source, expected.data(), expected.size()) != 0) return false;

    std::array<BYTE, Length> jump{};
    jump.fill(0x90);
    jump[0] = 0xe9;
    const auto displacement = static_cast<int32_t>(targetRva - sourceRva - 5);
    memcpy(jump.data() + 1, &displacement, sizeof(displacement));

    DWORD oldProtection{};
    if (!VirtualProtect(source, jump.size(), PAGE_EXECUTE_READWRITE, &oldProtection))
        return false;
    memcpy(source, jump.data(), jump.size());
    FlushInstructionCache(GetCurrentProcess(), source, jump.size());
    VirtualProtect(source, jump.size(), oldProtection, &oldProtection);
    return true;
}

bool InstallFieldRosterUiBypass()
{
    auto* image = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    if (!image) return false;
    if (memcmp(image + FieldRosterUiTailRva, ExpectedFieldRosterUiTail.data(),
            ExpectedFieldRosterUiTail.size()) != 0 ||
        memcmp(image + FieldCreateUiTailRva, ExpectedFieldCreateUiTail.data(),
            ExpectedFieldCreateUiTail.size()) != 0 ||
        memcmp(image + PlayGameUiPrimaryRva, ExpectedPlayGameUiPrimary.data(),
            ExpectedPlayGameUiPrimary.size()) != 0 ||
        memcmp(image + PlayGameUiControlsRva, ExpectedPlayGameUiControls.data(),
            ExpectedPlayGameUiControls.size()) != 0 ||
        memcmp(image + PlayGameUiSecondaryRva, ExpectedPlayGameUiSecondary.data(),
            ExpectedPlayGameUiSecondary.size()) != 0)
        return false;

    DWORD oldProtection{};
    BYTE* playGamePatch = image + PlayGameUiPrimaryRva;
    constexpr size_t PlayGamePatchLength =
        PlayGameUiSecondaryRva - PlayGameUiPrimaryRva + NoOperationCall.size();
    if (!InstallRelativeJump(
            image, FieldRosterUiTailRva, FieldRosterEpilogueRva,
            ExpectedFieldRosterUiTail) ||
        !InstallRelativeJump(
            image, FieldCreateUiTailRva, FieldCreateContinuationRva,
            ExpectedFieldCreateUiTail))
        return false;

    if (!VirtualProtect(
        playGamePatch, PlayGamePatchLength, PAGE_EXECUTE_READWRITE, &oldProtection))
        return false;
    memcpy(image + PlayGameUiPrimaryRva, NoOperationCall.data(), NoOperationCall.size());
    memcpy(image + PlayGameUiControlsRva, NoOperationCall.data(), NoOperationCall.size());
    memcpy(image + PlayGameUiSecondaryRva, NoOperationCall.data(), NoOperationCall.size());
    FlushInstructionCache(GetCurrentProcess(), playGamePatch, PlayGamePatchLength);
    VirtualProtect(playGamePatch, PlayGamePatchLength, oldProtection, &oldProtection);
    InterlockedExchange(&FieldRosterUiBypassInstalled, 1);
    return true;
}

bool InstallBattleHudRedrawBypass(BYTE* image)
{
    BYTE* target = image + BattleHudRedrawRva;
    if (memcmp(
            target, ExpectedBattleHudRedraw.data(),
            ExpectedBattleHudRedraw.size()) != 0)
        return false;

    DWORD oldProtection{};
    if (!VirtualProtect(
            target, ExpectedBattleHudRedraw.size(),
            PAGE_EXECUTE_READWRITE, &oldProtection))
        return false;
    memset(target, 0x90, ExpectedBattleHudRedraw.size());
    target[0] = 0xc3;
    FlushInstructionCache(
        GetCurrentProcess(), target, ExpectedBattleHudRedraw.size());
    VirtualProtect(
        target, ExpectedBattleHudRedraw.size(), oldProtection, &oldProtection);
    return true;
}

bool HasHeadlessInputApi(HMODULE engine)
{
    return GetProcAddress(engine, InputSymbol) &&
        GetProcAddress(engine, IsInputEnabledSymbol) &&
        GetProcAddress(engine, DisableInputSymbol) &&
        GetProcAddress(engine, ClearInputSymbol);
}

void SuppressHeadlessInput(HMODULE engine)
{
    auto** input = reinterpret_cast<void**>(GetProcAddress(engine, InputSymbol));
    if (!input || !*input) return;

    using IsInputEnabledFn = int(__thiscall*)(const void*);
    using InputActionFn = void(__thiscall*)(void*);
    auto isEnabled = reinterpret_cast<IsInputEnabledFn>(
        GetProcAddress(engine, IsInputEnabledSymbol));
    auto disable = reinterpret_cast<InputActionFn>(
        GetProcAddress(engine, DisableInputSymbol));
    auto clear = reinterpret_cast<InputActionFn>(
        GetProcAddress(engine, ClearInputSymbol));
    if (!isEnabled || !disable || !clear) return;

    bool disabled{};
    __try
    {
        if (isEnabled(*input) != 0)
        {
            disable(*input);
            disabled = true;
        }
        clear(*input);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return;
    }

    if (disabled &&
        InterlockedCompareExchange(&HeadlessInputSuppressed, 1, 0) == 0)
        CompatLog("headless: input do engine desativado e liberado ao cliente interativo");
}
}

bool ConfigureHeadlessEngine()
{
    if (!IsHeadlessRequested()) return true;

    HMODULE engine = GetModuleHandleW(L"engine.dll");
    if (!engine)
    {
        CompatLog("headless recusado: engine.dll ainda nao carregada");
        return false;
    }

    auto* dedicated = reinterpret_cast<volatile LONG*>(
        GetProcAddress(engine, DedicatedServerSymbol));
    if (!dedicated)
    {
        CompatLog("headless recusado: _bDedicatedServer ausente");
        return false;
    }
    if (!HasHeadlessInputApi(engine))
    {
        CompatLog("headless recusado: API CInput necessaria para isolamento ausente");
        return false;
    }
    auto* image = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    if (!image || !InstallBattleHudRedrawBypass(image))
    {
        CompatLog("headless recusado: redraw do HUD incompativel");
        return false;
    }

    InterlockedExchange(dedicated, 1);
    InterlockedExchange(&HeadlessActive, 1);
    AddVectoredExceptionHandler(1, &TraceHeadlessException);
    CompatLog("headless ativado antes do entry point (_bDedicatedServer=1)");
    return true;
}

void PollHeadlessEngineState()
{
    if (InterlockedCompareExchange(&HeadlessActive, 0, 0) == 0) return;
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    if (!engine) return;
    SuppressHeadlessInput(engine);
    if (InterlockedCompareExchange(&FieldRosterUiBypassInstalled, 0, 0) == 0 &&
        InstallFieldRosterUiBypass())
        CompatLog("headless: roster e Play Game prontos sem texturas da UI");
    auto** network = reinterpret_cast<void**>(GetProcAddress(engine, NetworkSymbol));
    using GetLocalPlayerCountFn = int(__thiscall*)(void*);
    auto getCount = reinterpret_cast<GetLocalPlayerCountFn>(
        GetProcAddress(engine, LocalPlayerCountSymbol));
    if (!network || !*network || !getCount) return;

    int count{};
    __try
    {
        count = getCount(*network);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return;
    }
    LONG previous = InterlockedExchange(&LastLocalPlayerCount, count);
    if (previous == count) return;
    char message[96]{};
    _snprintf_s(message, _countof(message), _TRUNCATE,
        "headless network pronto: localPlayerCount=%d", count);
    CompatLog(message);
}

void PumpHeadlessEngineFrame()
{
    if (InterlockedCompareExchange(&HeadlessActive, 0, 0) == 0) return;
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    if (!engine) return;
    auto** network = reinterpret_cast<void**>(GetProcAddress(engine, NetworkSymbol));
    using MainLoopFn = void(__thiscall*)(void*);
    auto mainLoop = reinterpret_cast<MainLoopFn>(
        GetProcAddress(engine, NetworkMainLoopSymbol));
    if (!network || !*network || !mainLoop) return;

    __try
    {
        PumpHeadlessBotAction();
        mainLoop(*network);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
    }
}

bool IsHeadlessFieldRosterReady()
{
    return InterlockedCompareExchange(
        &FieldRosterUiBypassInstalled, 0, 0) != 0;
}
