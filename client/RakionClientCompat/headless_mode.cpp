#include <windows.h>

#include "compat_log.h"
#include "headless_mode.h"

namespace
{
constexpr char HeadlessVariable[] = "OPENRAKION_HEADLESS";
constexpr char DedicatedServerSymbol[] = "?_bDedicatedServer@@3HA";

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
    CompatLog("headless ativado antes do entry point (_bDedicatedServer=1)");
    return true;
}
