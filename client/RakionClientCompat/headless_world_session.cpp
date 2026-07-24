#include <windows.h>

#include <cctype>
#include <cstdint>
#include <cstdio>
#include <cstring>
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
constexpr char FieldReadySymbol[] =
    "?SendFieldReady@IScavengerWorldNet@@UAEXE@Z";
constexpr char FieldPlayingSymbol[] =
    "?IsGamePlaying@FieldInfo@@QAEHXZ";
constexpr char FieldMasterSymbol[] =
    "?IsMasterSlot@FieldInfo@@QAEHXZ";
constexpr char FieldSeatMasterSymbol[] =
    "?IsMasterSlot@FieldInfo@@QAEHE@Z";
constexpr char FieldRoundStartSymbol[] =
    "?SendFieldGameRoundStart@IScavengerWorldNet@@UAEXXZ";
constexpr char FieldVariable[] = "OPENRAKION_HEADLESS_FIELD";
constexpr char WorldVariable[] = "OPENRAKION_HEADLESS_WORLD";
constexpr char AssignStringSymbol[] = "??4CTString@@QAEAAV0@PBD@Z";
constexpr char StringConstructorSymbol[] = "??0CTString@@QAE@PBD@Z";
constexpr char StringDestructorSymbol[] = "??1CTString@@QAE@XZ";
constexpr char PlayerConstructorSymbol[] =
    "??0CPlayerCharacter@@QAE@ABVCTString@@0@Z";
constexpr char PlayerDestructorSymbol[] = "??1CPlayerCharacter@@QAE@XZ";
constexpr char NetworkSessionConstructorSymbol[] =
    "??0CNetworkSession@@QAE@ABVCTString@@J@Z";
constexpr char NetworkSessionDestructorSymbol[] =
    "??1CNetworkSession@@QAE@XZ";
constexpr char FileNameCopyConstructorSymbol[] =
    "??0CTFileName@@QAE@ABV0@@Z";
constexpr unsigned WorldPortNetworkOrder = 0x049f;
constexpr unsigned char SkipHashVerification = 4;
constexpr size_t AccountSlotCountOffset = 0x6c;
constexpr size_t CharacterRecordsOffset = 0x1338;
constexpr size_t CharacterRecordSize = 0x424;
constexpr size_t CharacterRecordNameOffset = 0x10;
constexpr unsigned char MaximumVisibleCharacters = 4;
constexpr uintptr_t ApplicationPointerRva = 0xfeed0;
constexpr size_t MenuStateOffset = 0x180;
constexpr size_t GamePointerOffset = 0x198;
constexpr size_t GameWorldNameOffset = 0x48;
constexpr size_t GameSessionNameOffset = 0x54;
constexpr size_t GamePlayerCharactersOffset = 0x130;
constexpr size_t GameMenuSplitScreenConfigOffset = 0x478;
constexpr size_t GameStartSplitScreenConfigOffset = 0x47c;
constexpr size_t GamePrimaryPlayerIndexOffset = 0x4b8;
constexpr size_t FieldPlayerRecordsOffset = 0x1ac;
constexpr size_t FieldPlayerRecordSize = 0x378;
constexpr size_t FieldPlayerAddressOffset = 0x34;
constexpr size_t FieldPlayerObservedPortOffset = 0x38;
constexpr size_t FieldPlayerAdvertisedPortOffset = 0x3a;
constexpr unsigned char FieldPlayerCount = 20;
constexpr size_t JoinGameVtableOffset = 0x9c;
constexpr size_t ApplicationServerWorldOffset = 0x64;
constexpr int PlayGameMenuState = 0x1d;
constexpr int PeerToPeerClientMode = 4;
constexpr DWORD FieldEnterDelayMilliseconds = 1500;
constexpr DWORD FieldReadyDelayMilliseconds = 1500;
constexpr DWORD EngineStartDelayMilliseconds = 500;
constexpr DWORD EngineRetryDelayMilliseconds = 2000;
constexpr LONG MaximumEngineStartAttempts = 3;
constexpr UINT StartEngineMessage = WM_APP + 0x271;
volatile LONG SessionState{};
volatile LONG CharacterReadyTick{};
volatile LONG FieldEnterTick{};
volatile LONG EngineStartTick{};
volatile LONG EngineWaitingLogged{};
volatile LONG EngineStartPending{};
volatile LONG EngineStartAttempts{};
volatile LONG EngineEndpointWaitingLogged{};
HWND GameWindow{};
WNDPROC OriginalWindowProcedure{};
char SelectedCharacterName[13]{};

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

void* GetFieldInfo(void* world)
{
    if (!world) return nullptr;
    using GetFieldInfoFn = void*(__thiscall*)(void*);
    auto** vtable = *reinterpret_cast<void***>(world);
    auto getField = vtable
        ? reinterpret_cast<GetFieldInfoFn>(vtable[2]) : nullptr;
    return getField ? getField(world) : nullptr;
}

int GetPeerToPeerMode()
{
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    void* world = engine ? GetWorld(engine) : nullptr;
    void* field = GetFieldInfo(world);
    using IsMasterFn = int(__thiscall*)(void*);
    auto isMaster = engine ? reinterpret_cast<IsMasterFn>(
        GetProcAddress(engine, FieldMasterSymbol)) : nullptr;
    if (!field || !isMaster) return 0;
    return isMaster(field) != 0 ? 0 : PeerToPeerClientMode;
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
        __try
        {
            size_t length{};
            const char* name = reinterpret_cast<const char*>(
                record + CharacterRecordNameOffset);
            while (length < sizeof(SelectedCharacterName) - 1 &&
                name[length] >= 0x20 && name[length] <= 0x7e)
                ++length;
            if (length == 0 || name[length] != '\0') continue;
            memcpy(SelectedCharacterName, name, length);
            SelectedCharacterName[length] = '\0';
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            continue;
        }
        select(world, characterId);
        char message[96]{};
        _snprintf_s(message, _countof(message), _TRUNCATE,
            "headless World: primeiro personagem selecionado (%s)",
            SelectedCharacterName);
        CompatLog(message);
        return 1;
    }
    return 0;
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

bool SetFieldReady()
{
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    void* world = engine ? GetWorld(engine) : nullptr;
    using FieldReadyFn = void(__thiscall*)(void*, unsigned char);
    auto ready = engine ? reinterpret_cast<FieldReadyFn>(
        GetProcAddress(engine, FieldReadySymbol)) : nullptr;
    if (!world || !ready) return false;
    ready(world, 1);
    CompatLog("headless World: estado ready enviado");
    return true;
}

bool IsFieldPlaying()
{
    auto* image = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    auto** application = image ? reinterpret_cast<BYTE**>(
        image + ApplicationPointerRva) : nullptr;
    if (application && *application &&
        *reinterpret_cast<int*>(*application + MenuStateOffset) == PlayGameMenuState)
        return true;

    HMODULE engine = GetModuleHandleW(L"engine.dll");
    void* world = engine ? GetWorld(engine) : nullptr;
    using IsPlayingFn = int(__thiscall*)(void*);
    auto isPlaying = engine ? reinterpret_cast<IsPlayingFn>(
        GetProcAddress(engine, FieldPlayingSymbol)) : nullptr;
    if (!world || !isPlaying) return false;
    void* field = GetFieldInfo(world);
    return field && isPlaying(field) != 0;
}

bool RequestFieldRoundStart()
{
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    void* world = engine ? GetWorld(engine) : nullptr;
    using RoundStartFn = void(__thiscall*)(void*);
    auto request = engine ? reinterpret_cast<RoundStartFn>(
        GetProcAddress(engine, FieldRoundStartSymbol)) : nullptr;
    if (!world || !request) return false;
    request(world);
    CompatLog("headless World: estado inicial do round solicitado");
    return true;
}

const char* ReadGameString(BYTE* game, size_t offset)
{
    auto** value = reinterpret_cast<const char**>(game + offset);
    return value ? *value : nullptr;
}

bool IsValidWorldName(const char* value)
{
    if (!value || value[0] == '\0' || strlen(value) >= MAX_PATH) return false;
    constexpr char Prefix[] = "LevelsSV\\";
    constexpr char Suffix[] = ".wld";
    const size_t length = strlen(value);
    return _strnicmp(value, Prefix, sizeof(Prefix) - 1) == 0 &&
        length > sizeof(Suffix) - 1 &&
        _stricmp(value + length - (sizeof(Suffix) - 1), Suffix) == 0 &&
        strstr(value, "..") == nullptr && strchr(value, ':') == nullptr;
}

bool AssignGameString(void* target, const char* value)
{
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    using AssignStringFn = void*(__thiscall*)(void*, const char*);
    auto assign = engine ? reinterpret_cast<AssignStringFn>(
        GetProcAddress(engine, AssignStringSymbol)) : nullptr;
    if (!assign || !value || value[0] == '\0') return false;
    assign(target, value);
    return true;
}

bool IsPlaceholderJoinAddress(const char* value)
{
    return !value || value[0] == '\0' ||
        _stricmp(value, "serveraddress") == 0 ||
        _stricmp(value, "serveraddress:0") == 0;
}

unsigned ReadNetworkPort(const BYTE* value)
{
    return static_cast<unsigned>(value[0]) << 8 | value[1];
}

bool EnsureGameJoinAddress(BYTE* game)
{
    void* target = game + GameSessionNameOffset + sizeof(void*);
    if (!IsPlaceholderJoinAddress(ReadGameString(game,
        GameSessionNameOffset + sizeof(void*))))
        return true;

    HMODULE engine = GetModuleHandleW(L"engine.dll");
    void* field = engine ? GetFieldInfo(GetWorld(engine)) : nullptr;
    using IsMasterFn = int(__thiscall*)(void*, unsigned char);
    auto isMaster = engine ? reinterpret_cast<IsMasterFn>(
        GetProcAddress(engine, FieldSeatMasterSymbol)) : nullptr;
    if (!field || !isMaster) return false;

    for (unsigned char seat = 0; seat < FieldPlayerCount; ++seat)
    {
        if (isMaster(field, seat) == 0) continue;
        auto* record = static_cast<BYTE*>(field) +
            FieldPlayerRecordsOffset + seat * FieldPlayerRecordSize;
        const BYTE* address = record + FieldPlayerAddressOffset;
        unsigned port = ReadNetworkPort(
            record + FieldPlayerAdvertisedPortOffset);
        if (port == 0)
            port = ReadNetworkPort(record + FieldPlayerObservedPortOffset);
        if (port == 0 || (address[0] | address[1] | address[2] | address[3]) == 0)
            return false;

        char endpoint[32]{};
        _snprintf_s(endpoint, _countof(endpoint), _TRUNCATE,
            "%u.%u.%u.%u:%u", address[0], address[1],
            address[2], address[3], port);
        if (!AssignGameString(target, endpoint)) return false;
        CompatLog("headless engine: endpoint P2P do master aplicado");
        return true;
    }
    return false;
}

bool EnsureGameWorldName(BYTE* application, BYTE* game)
{
    const char* current = ReadGameString(game, GameWorldNameOffset);
    if (current && current[0] != '\0') return true;

    const char* fromApplication = ReadGameString(
        application, ApplicationServerWorldOffset);
    if (IsValidWorldName(fromApplication))
        return AssignGameString(game + GameWorldNameOffset, fromApplication);

    char configured[MAX_PATH]{};
    const DWORD length = GetEnvironmentVariableA(
        WorldVariable, configured, static_cast<DWORD>(sizeof(configured)));
    if (length == 0 || length >= sizeof(configured) || !IsValidWorldName(configured))
        return false;
    return AssignGameString(game + GameWorldNameOffset, configured);
}

bool PrepareLocalPlayerCharacter(BYTE* game)
{
    if (SelectedCharacterName[0] == '\0') return false;
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    if (!engine) return false;

    using StringConstructorFn = void*(__thiscall*)(void*, const char*);
    using StringDestructorFn = void(__thiscall*)(void*);
    using PlayerConstructorFn = void*(__thiscall*)(void*, const void*, const void*);
    using PlayerDestructorFn = void(__thiscall*)(void*);
    auto stringConstructor = reinterpret_cast<StringConstructorFn>(
        GetProcAddress(engine, StringConstructorSymbol));
    auto stringDestructor = reinterpret_cast<StringDestructorFn>(
        GetProcAddress(engine, StringDestructorSymbol));
    auto playerConstructor = reinterpret_cast<PlayerConstructorFn>(
        GetProcAddress(engine, PlayerConstructorSymbol));
    auto playerDestructor = reinterpret_cast<PlayerDestructorFn>(
        GetProcAddress(engine, PlayerDestructorSymbol));
    if (!stringConstructor || !stringDestructor ||
        !playerConstructor || !playerDestructor)
        return false;

    void* name{};
    void* species{};
    stringConstructor(&name, SelectedCharacterName);
    stringConstructor(&species, "Human");
    BYTE* player = game + GamePlayerCharactersOffset;
    playerDestructor(player);
    playerConstructor(player, &name, &species);
    stringDestructor(&species);
    stringDestructor(&name);
    CompatLog("headless engine: personagem local preparado");
    return true;
}

struct FileNameValue
{
    void* string{};
    void* preloaded{};
};

bool JoinFieldEngine(BYTE* game)
{
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    if (!engine) return false;

    using NetworkSessionConstructorFn =
        void*(__thiscall*)(void*, const void*, long);
    using NetworkSessionDestructorFn = void(__thiscall*)(void*);
    using FileNameCopyConstructorFn = void*(__thiscall*)(void*, const void*);
    using JoinGameFn = int(__thiscall*)(void*, const void*, FileNameValue);
    auto constructSession = reinterpret_cast<NetworkSessionConstructorFn>(
        GetProcAddress(engine, NetworkSessionConstructorSymbol));
    auto destroySession = reinterpret_cast<NetworkSessionDestructorFn>(
        GetProcAddress(engine, NetworkSessionDestructorSymbol));
    auto copyFileName = reinterpret_cast<FileNameCopyConstructorFn>(
        GetProcAddress(engine, FileNameCopyConstructorSymbol));
    auto** vtable = *reinterpret_cast<void***>(game);
    auto joinGame = vtable ? reinterpret_cast<JoinGameFn>(
        vtable[JoinGameVtableOffset / sizeof(void*)]) : nullptr;
    if (!constructSession || !destroySession || !copyFileName || !joinGame)
        return false;

    alignas(void*) BYTE session[512]{};
    FileNameValue world{};
    constructSession(session,
        game + GameSessionNameOffset + sizeof(void*), 0);
    copyFileName(&world, game + GameWorldNameOffset);
    const int joined = joinGame(game, session, world);
    destroySession(session);
    return joined != 0;
}

bool StartFieldEngine()
{
    auto* image = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    auto** application = image ? reinterpret_cast<BYTE**>(
        image + ApplicationPointerRva) : nullptr;
    BYTE* game = application && *application
        ? *reinterpret_cast<BYTE**>(*application + GamePointerOffset) : nullptr;
    if (!game)
    {
        CompatLog("headless engine recusado: CGame indisponivel");
        return false;
    }

    const char* worldName{};
    const char* sessionName{};
    const char* joinAddress{};
    int playerIndex{-1};
    int peerMode{};
    __try
    {
        if (!EnsureGameWorldName(*application, game))
        {
            if (InterlockedCompareExchange(&EngineWaitingLogged, 1, 0) == 0)
                CompatLog("headless engine aguardando mundo configurado");
            return false;
        }
        peerMode = GetPeerToPeerMode();
        if (peerMode == PeerToPeerClientMode && !EnsureGameJoinAddress(game))
        {
            if (InterlockedCompareExchange(
                &EngineEndpointWaitingLogged, 1, 0) == 0)
                CompatLog("headless engine aguardando endpoint P2P do master");
            return false;
        }
        worldName = ReadGameString(game, GameWorldNameOffset);
        sessionName = ReadGameString(game, GameSessionNameOffset);
        joinAddress = ReadGameString(game, GameSessionNameOffset + sizeof(void*));
        playerIndex = *reinterpret_cast<int*>(
            game + GamePrimaryPlayerIndexOffset);
        if (!worldName || worldName[0] == '\0' ||
            !sessionName || sessionName[0] == '\0' || playerIndex < 0 ||
            peerMode == 0 ||
            (peerMode == PeerToPeerClientMode &&
                (!joinAddress || joinAddress[0] == '\0')))
        {
            if (InterlockedCompareExchange(&EngineWaitingLogged, 1, 0) == 0)
                CompatLog("headless engine aguardando sessao e jogador local");
            return false;
        }
        *reinterpret_cast<int*>(game + GameMenuSplitScreenConfigOffset) = 0;
        *reinterpret_cast<int*>(game + GameStartSplitScreenConfigOffset) = 0;
        *reinterpret_cast<int*>(game + GamePrimaryPlayerIndexOffset) = 0;
        if (!PrepareLocalPlayerCharacter(game))
        {
            CompatLog("headless engine recusado: personagem local invalido");
            return false;
        }

    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        CompatLog("headless engine recusado: ABI de CGame incompatível");
        return false;
    }

    char attempt[320]{};
    _snprintf_s(attempt, _countof(attempt), _TRUNCATE,
        "headless engine: iniciando mode=%d world=%s session=%s peer=%s",
        peerMode, worldName, sessionName,
        peerMode == PeerToPeerClientMode ? joinAddress : "<master>");
    CompatLog(attempt);

    bool joined{};
    __try
    {
        joined = JoinFieldEngine(game);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        CompatLog("headless engine recusado: ABI de CGame::JoinGame incompatível");
        return false;
    }
    if (!joined)
    {
        CompatLog("headless engine recusado: CGame::JoinGame falhou");
        return false;
    }

    char message[256]{};
    _snprintf_s(message, _countof(message), _TRUNCATE,
        "headless engine iniciado: mode=%d world=%s session=%s playerIndex=%d",
        peerMode, worldName, sessionName, playerIndex);
    CompatLog(message);
    return true;
}

LRESULT CALLBACK HeadlessWindowProcedure(
    HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    if (message == StartEngineMessage)
    {
        const LONG attempt = InterlockedIncrement(&EngineStartAttempts);
        const bool started = StartFieldEngine();
        InterlockedExchange(&EngineStartTick, static_cast<LONG>(GetTickCount()));
        InterlockedExchange(&SessionState,
            started ? 12 : attempt < MaximumEngineStartAttempts ? 15 : 13);
        InterlockedExchange(&EngineStartPending, 0);
        return 0;
    }
    return CallWindowProcA(
        OriginalWindowProcedure, window, message, wParam, lParam);
}

BOOL CALLBACK FindProcessWindow(HWND window, LPARAM)
{
    DWORD processId{};
    GetWindowThreadProcessId(window, &processId);
    if (processId != GetCurrentProcessId()) return TRUE;
    GameWindow = window;
    return FALSE;
}

bool EnsureMainThreadDispatcher()
{
    if (GameWindow && OriginalWindowProcedure) return true;
    GameWindow = nullptr;
    EnumWindows(&FindProcessWindow, 0);
    if (!GameWindow) return false;

    SetLastError(ERROR_SUCCESS);
    const LONG_PTR original = SetWindowLongPtrA(
        GameWindow, GWLP_WNDPROC,
        reinterpret_cast<LONG_PTR>(&HeadlessWindowProcedure));
    if (original == 0 && GetLastError() != ERROR_SUCCESS)
    {
        GameWindow = nullptr;
        return false;
    }
    OriginalWindowProcedure = reinterpret_cast<WNDPROC>(original);
    CompatLog("headless engine: dispatcher da thread principal instalado");
    return true;
}

bool QueueFieldEngineStart()
{
    if (!EnsureMainThreadDispatcher()) return false;
    if (InterlockedCompareExchange(&EngineStartPending, 1, 0) != 0) return true;
    if (PostMessageA(GameWindow, StartEngineMessage, 0, 0) != FALSE) return true;
    InterlockedExchange(&EngineStartPending, 0);
    return false;
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
        if (!JoinConfiguredField())
        {
            InterlockedExchange(&SessionState, 5);
            return;
        }
        InterlockedExchange(&FieldEnterTick, static_cast<LONG>(GetTickCount()));
        InterlockedExchange(&SessionState, 6);
        return;
    }
    if (state == 6)
    {
        const DWORD enteredAt = static_cast<DWORD>(
            InterlockedCompareExchange(&FieldEnterTick, 0, 0));
        if (GetTickCount() - enteredAt < FieldReadyDelayMilliseconds) return;
        InterlockedExchange(&SessionState, SetFieldReady() ? 8 : 7);
        return;
    }
    if (state == 8 && IsFieldPlaying())
    {
        if (!RequestFieldRoundStart())
        {
            InterlockedExchange(&SessionState, 13);
            return;
        }
        InterlockedExchange(&EngineStartTick, static_cast<LONG>(GetTickCount()));
        InterlockedExchange(&SessionState, 10);
        return;
    }
    if (state == 10)
    {
        const DWORD requestedAt = static_cast<DWORD>(
            InterlockedCompareExchange(&EngineStartTick, 0, 0));
        if (GetTickCount() - requestedAt < EngineStartDelayMilliseconds) return;
        if (QueueFieldEngineStart()) InterlockedExchange(&SessionState, 11);
        return;
    }
    if (state == 12)
    {
        InterlockedExchange(&SessionState, 14);
        return;
    }
    if (state == 15)
    {
        const DWORD failedAt = static_cast<DWORD>(
            InterlockedCompareExchange(&EngineStartTick, 0, 0));
        if (GetTickCount() - failedAt < EngineRetryDelayMilliseconds) return;
        if (QueueFieldEngineStart()) InterlockedExchange(&SessionState, 11);
    }
}
