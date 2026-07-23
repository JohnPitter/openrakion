#include <windows.h>

#include <cctype>
#include <cstdint>
#include <cstdio>
#include <string>
#include <utility>
#include <vector>

#include "bot_telemetry.h"
#include "compat_log.h"
#include "headless_mode.h"
#include "headless_world_session.h"

namespace
{
constexpr char HeadlessVariable[] = "OPENRAKION_HEADLESS";
constexpr char WorldNetSymbol[] = "?_pRakionWorldNet@@3PAVIScavengerWorldNet@@A";
constexpr char ConnectSymbol[] = "?Connect@IScavengerWorldNet@@UAEEKIAAK@Z";
constexpr char SendLoginSymbol[] = "?SendLogin@IScavengerWorldNet@@UAEXPAD0G0E@Z";
constexpr char AccountInfoSymbol[] =
    "?GetAccountInfo@IScavengerWorldNet@@QAEPAUAccountInfo_s@@XZ";
constexpr char CharacterSelectSymbol[] =
    "?SendCharacterSelect@IScavengerWorldNet@@UAEXK@Z";
constexpr char FieldEnterSymbol[] =
    "?SendFieldEnter@IScavengerWorldNet@@UAEXGPAD@Z";
constexpr char FieldVariable[] = "OPENRAKION_HEADLESS_FIELD";
constexpr unsigned WorldPortNetworkOrder = 0x049f;
constexpr unsigned char SkipHashVerification = 4;
constexpr size_t AccountSlotCountOffset = 0x6c;
constexpr size_t CharacterRecordsOffset = 0x1338;
constexpr size_t CharacterRecordSize = 0x424;
constexpr unsigned char MaximumVisibleCharacters = 4;
constexpr DWORD FieldEnterDelayMilliseconds = 1500;
volatile LONG SessionState{};
volatile LONG CharacterReadyTick{};

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

void* GetWorld(HMODULE engine)
{
    auto** world = reinterpret_cast<void**>(GetProcAddress(engine, WorldNetSymbol));
    return world ? *world : nullptr;
}

bool StartWorldSession()
{
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    if (!engine) return false;
    void* world = GetWorld(engine);
    if (!world) return false;

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
    connect(world, address, WorldPortNetworkOrder, localAddress);
    char emptyHash[] = "";
    sendLogin(world, arguments[0].data(), arguments[1].data(), 0,
        emptyHash, SkipHashVerification);
    CompatLog("headless World: conexão direta e login enviados");
    return true;
}

int SelectFirstCharacter()
{
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    void* world = engine ? GetWorld(engine) : nullptr;
    using GetAccountFn = void*(__thiscall*)(void*);
    using SelectFn = void(__thiscall*)(void*, unsigned long);
    auto getAccount = engine ? reinterpret_cast<GetAccountFn>(
        GetProcAddress(engine, AccountInfoSymbol)) : nullptr;
    auto select = engine ? reinterpret_cast<SelectFn>(
        GetProcAddress(engine, CharacterSelectSymbol)) : nullptr;
    if (!world || !getAccount || !select) return 0;

    auto* account = static_cast<unsigned char*>(getAccount(world));
    if (!account) return 0;
    const unsigned char count = account[AccountSlotCountOffset];
    if (count == 0) return 0;
    const unsigned char limit = count < MaximumVisibleCharacters
        ? count : MaximumVisibleCharacters;
    for (unsigned char slot = 0; slot < limit; ++slot)
    {
        auto* record = account + CharacterRecordsOffset + slot * CharacterRecordSize;
        const unsigned long characterId = *reinterpret_cast<unsigned long*>(record);
        if (characterId == 0) continue;
        select(world, characterId);
        CompatLog("headless World: primeiro personagem selecionado");
        return 1;
    }
    CompatLog("headless World recusado: conta sem personagem");
    return -1;
}

bool IsCharacterReady()
{
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    void* world = engine ? GetWorld(engine) : nullptr;
    if (!world) return false;
    using GetSelectedFn = void*(__thiscall*)(void*);
    auto** vtable = *reinterpret_cast<void***>(world);
    return vtable && reinterpret_cast<GetSelectedFn>(vtable[3])(world) != nullptr;
}

bool JoinConfiguredField()
{
    char configured[16]{};
    const DWORD length = GetEnvironmentVariableA(
        FieldVariable, configured, static_cast<DWORD>(sizeof(configured)));
    unsigned fieldId{};
    char tail{};
    if (length == 0 || length >= sizeof(configured) ||
        sscanf_s(configured, "%u%c", &fieldId, &tail, 1) != 1 ||
        fieldId == 0 || fieldId > 0xffff)
    {
        CompatLog("headless World recusado: field invalido");
        return false;
    }

    HMODULE engine = GetModuleHandleW(L"engine.dll");
    void* world = engine ? GetWorld(engine) : nullptr;
    using FieldEnterFn = void(__thiscall*)(void*, unsigned short, char*);
    auto enter = engine ? reinterpret_cast<FieldEnterFn>(
        GetProcAddress(engine, FieldEnterSymbol)) : nullptr;
    if (!world || !enter) return false;
    char emptyPassword[] = "";
    enter(world, static_cast<unsigned short>(fieldId), emptyPassword);
    CompatLog("headless World: entrada no field enviada");
    return true;
}
}

void PollHeadlessWorldSession()
{
    if (!IsHeadlessRequested()) return;
    const LONG state = InterlockedCompareExchange(&SessionState, 0, 0);
    if (state == 0 && InterlockedCompareExchange(&SessionState, 1, 0) == 0)
    {
        InterlockedExchange(&SessionState, StartWorldSession() ? 2 : 0);
        return;
    }
    if (state == 2)
    {
        const int selected = SelectFirstCharacter();
        if (selected != 0) InterlockedExchange(&SessionState, selected > 0 ? 3 : 5);
        return;
    }
    if (state == 3 && IsCharacterReady())
    {
        InterlockedExchange(&CharacterReadyTick, static_cast<LONG>(GetTickCount()));
        InterlockedExchange(&SessionState, 4);
        CompatLog("headless World: personagem confirmado pelo engine");
        return;
    }
    if (state == 4)
    {
        if (!IsHeadlessFieldRosterReady()) return;
        const DWORD readyAt = static_cast<DWORD>(
            InterlockedCompareExchange(&CharacterReadyTick, 0, 0));
        if (GetTickCount() - readyAt < FieldEnterDelayMilliseconds) return;
        InterlockedExchange(&SessionState, JoinConfiguredField() ? 6 : 5);
    }
}
