#include <windows.h>

#include <cstdio>

#include "compat_log.h"
#include "headless_mode.h"

namespace
{
constexpr char HeadlessVariable[] = "OPENRAKION_HEADLESS";
constexpr char DedicatedServerSymbol[] = "?_bDedicatedServer@@3HA";
constexpr char NetworkSymbol[] = "?_pNetwork@@3PAVCNetworkLibrary@@A";
constexpr char LocalPlayerCountSymbol[] =
    "?GetLocalPlayerCount@CNetworkLibrary@@QAEJXZ";
volatile LONG HeadlessActive{};
volatile LONG LastLocalPlayerCount{-1};

bool IsHeadlessRequested()
{
    char value[8]{};
    const DWORD length = GetEnvironmentVariableA(
        HeadlessVariable, value, static_cast<DWORD>(sizeof(value)));
    return length == 1 && value[0] == '1';
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
