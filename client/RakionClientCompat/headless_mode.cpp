#include <windows.h>

#include <array>
#include <cstdio>
#include <cstdint>
#include <cstring>

#include "compat_log.h"
#include "headless_mode.h"

namespace
{
constexpr char HeadlessVariable[] = "OPENRAKION_HEADLESS";
constexpr char DedicatedServerSymbol[] = "?_bDedicatedServer@@3HA";
constexpr char NetworkSymbol[] = "?_pNetwork@@3PAVCNetworkLibrary@@A";
constexpr char LocalPlayerCountSymbol[] =
    "?GetLocalPlayerCount@CNetworkLibrary@@QAEJXZ";
constexpr uintptr_t FieldRosterUiTailRva = 0x7a4fc;
constexpr uintptr_t FieldRosterEpilogueRva = 0x7a57e;
constexpr uintptr_t PlayGameUiPrimaryRva = 0x60ef1;
constexpr uintptr_t PlayGameUiControlsRva = 0x60ef8;
constexpr uintptr_t PlayGameUiSecondaryRva = 0x60eff;
constexpr std::array<BYTE, 6> ExpectedFieldRosterUiTail{
    0x8b, 0x0d, 0xd0, 0xee, 0x4f, 0x00
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
volatile LONG HeadlessActive{};
volatile LONG LastLocalPlayerCount{-1};
volatile LONG FieldRosterUiBypassInstalled{};

bool IsHeadlessRequested()
{
    char value[8]{};
    const DWORD length = GetEnvironmentVariableA(
        HeadlessVariable, value, static_cast<DWORD>(sizeof(value)));
    return length == 1 && value[0] == '1';
}

bool InstallFieldRosterUiBypass()
{
    auto* image = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    if (!image) return false;
    if (memcmp(image + FieldRosterUiTailRva, ExpectedFieldRosterUiTail.data(),
            ExpectedFieldRosterUiTail.size()) != 0 ||
        memcmp(image + PlayGameUiPrimaryRva, ExpectedPlayGameUiPrimary.data(),
            ExpectedPlayGameUiPrimary.size()) != 0 ||
        memcmp(image + PlayGameUiControlsRva, ExpectedPlayGameUiControls.data(),
            ExpectedPlayGameUiControls.size()) != 0 ||
        memcmp(image + PlayGameUiSecondaryRva, ExpectedPlayGameUiSecondary.data(),
            ExpectedPlayGameUiSecondary.size()) != 0)
        return false;

    std::array<BYTE, 6> jump{0xe9, 0, 0, 0, 0, 0x90};
    const auto displacement = static_cast<int32_t>(
        FieldRosterEpilogueRva - FieldRosterUiTailRva - 5);
    memcpy(jump.data() + 1, &displacement, sizeof(displacement));

    DWORD oldProtection{};
    BYTE* rosterPatch = image + FieldRosterUiTailRva;
    BYTE* playGamePatch = image + PlayGameUiPrimaryRva;
    constexpr size_t PlayGamePatchLength =
        PlayGameUiSecondaryRva - PlayGameUiPrimaryRva + NoOperationCall.size();
    if (!VirtualProtect(rosterPatch, jump.size(), PAGE_EXECUTE_READWRITE, &oldProtection))
        return false;
    memcpy(rosterPatch, jump.data(), jump.size());
    FlushInstructionCache(GetCurrentProcess(), rosterPatch, jump.size());
    VirtualProtect(rosterPatch, jump.size(), oldProtection, &oldProtection);

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

    InterlockedExchange(dedicated, 1);
    InterlockedExchange(&HeadlessActive, 1);
    CompatLog("headless ativado antes do entry point (_bDedicatedServer=1)");
    return true;
}

void PollHeadlessEngineState()
{
    if (InterlockedCompareExchange(&HeadlessActive, 0, 0) == 0) return;
    if (InterlockedCompareExchange(&FieldRosterUiBypassInstalled, 0, 0) == 0 &&
        InstallFieldRosterUiBypass())
        CompatLog("headless: roster e Play Game prontos sem texturas da UI");
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    if (!engine) return;
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

bool IsHeadlessFieldRosterReady()
{
    return InterlockedCompareExchange(
        &FieldRosterUiBypassInstalled, 0, 0) != 0;
}
