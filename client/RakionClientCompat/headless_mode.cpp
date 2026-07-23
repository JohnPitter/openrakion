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
constexpr std::array<BYTE, 6> ExpectedFieldRosterUiTail{
    0x8b, 0x0d, 0xd0, 0xee, 0x4f, 0x00
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
    BYTE* patch = image + FieldRosterUiTailRva;
    if (memcmp(patch, ExpectedFieldRosterUiTail.data(),
        ExpectedFieldRosterUiTail.size()) != 0)
    {
        return false;
    }

    std::array<BYTE, 6> jump{0xe9, 0, 0, 0, 0, 0x90};
    const auto displacement = static_cast<int32_t>(
        FieldRosterEpilogueRva - FieldRosterUiTailRva - 5);
    memcpy(jump.data() + 1, &displacement, sizeof(displacement));

    DWORD oldProtection{};
    if (!VirtualProtect(patch, jump.size(), PAGE_EXECUTE_READWRITE, &oldProtection))
        return false;
    memcpy(patch, jump.data(), jump.size());
    FlushInstructionCache(GetCurrentProcess(), patch, jump.size());
    VirtualProtect(patch, jump.size(), oldProtection, &oldProtection);
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
        CompatLog("headless: callback de roster pronto sem construcao da UI");
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
