#include <windows.h>

#include <cctype>
#include <cstdint>
#include <string>
#include <utility>
#include <vector>

#include "bot_telemetry.h"
#include "compat_log.h"
#include "headless_world_session.h"

namespace
{
constexpr char HeadlessVariable[] = "OPENRAKION_HEADLESS";
constexpr char WorldNetSymbol[] = "?_pRakionWorldNet@@3PAVIScavengerWorldNet@@A";
constexpr char ConnectSymbol[] = "?Connect@IScavengerWorldNet@@UAEEKIAAK@Z";
constexpr char SendLoginSymbol[] = "?SendLogin@IScavengerWorldNet@@UAEXPAD0G0E@Z";
constexpr unsigned WorldPortNetworkOrder = 0x049f;
constexpr unsigned char SkipHashVerification = 4;
volatile LONG SessionState{};

bool IsHeadlessRequested()
{
    char value[8]{};
    const DWORD length = GetEnvironmentVariableA(
        HeadlessVariable, value, static_cast<DWORD>(sizeof(value)));
    return length == 1 && value[0] == '1';
}

std::vector<std::string> ReadLegacyArguments()
{
    std::vector<std::string> values;
    const char* command = GetCommandLineA();
    while (command && *command)
    {
        while (*command && std::isspace(static_cast<unsigned char>(*command))) ++command;
        if (!*command) break;
        const char* start = command;
        while (*command && !std::isspace(static_cast<unsigned char>(*command))) ++command;
        values.emplace_back(start, command);
    }
    return values;
}

int HexValue(char value)
{
    if (value >= '0' && value <= '9') return value - '0';
    if (value >= 'a' && value <= 'f') return value - 'a' + 10;
    if (value >= 'A' && value <= 'F') return value - 'A' + 10;
    return -1;
}

bool DecodeCredential(std::string& credential)
{
    if (credential.empty() || credential.size() % 2 != 0) return false;
    std::string decoded;
    decoded.reserve(credential.size() / 2);
    for (size_t index = 0; index < credential.size(); index += 2)
    {
        const int high = HexValue(credential[index]);
        const int low = HexValue(credential[index + 1]);
        if (high < 0 || low < 0) return false;
        decoded.push_back(static_cast<char>(high << 4 | low));
    }
    credential = std::move(decoded);
    return true;
}

bool StartWorldSession()
{
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    if (!engine) return false;
    auto** world = reinterpret_cast<void**>(GetProcAddress(engine, WorldNetSymbol));
    if (!world || !*world) return false;

    std::vector<std::string> arguments = ReadLegacyArguments();
    if (arguments.size() < 2)
    {
        CompatLog("headless World recusado: argumentos de login ausentes");
        return true;
    }
    if (!DecodeCredential(arguments[1]))
    {
        CompatLog("headless World recusado: credencial codificada invalida");
        return true;
    }

    uint32_t address{};
    if (!TryGetServerAddress(address))
    {
        CompatLog("headless World recusado: server.host indisponivel");
        return true;
    }

    using ConnectFn = unsigned char(__thiscall*)(void*, unsigned long, unsigned, unsigned long&);
    using SendLoginFn = void(__thiscall*)(
        void*, char*, char*, unsigned short, char*, unsigned char);
    auto connect = reinterpret_cast<ConnectFn>(GetProcAddress(engine, ConnectSymbol));
    auto sendLogin = reinterpret_cast<SendLoginFn>(GetProcAddress(engine, SendLoginSymbol));
    if (!connect || !sendLogin)
    {
        CompatLog("headless World recusado: ABI de login incompatível");
        return true;
    }

    unsigned long localAddress{};
    connect(*world, address, WorldPortNetworkOrder, localAddress);
    char emptyHash[] = "";
    sendLogin(*world, arguments[0].data(), arguments[1].data(), 0,
        emptyHash, SkipHashVerification);
    CompatLog("headless World: conexão direta e login enviados");
    return true;
}
}

void PollHeadlessWorldSession()
{
    if (!IsHeadlessRequested()) return;
    if (InterlockedCompareExchange(&SessionState, 1, 0) != 0) return;
    if (StartWorldSession()) InterlockedExchange(&SessionState, 2);
    else InterlockedExchange(&SessionState, 0);
}
