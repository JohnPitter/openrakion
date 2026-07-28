#include "engine_runtime.h"

#include <array>
#include <fstream>
#include <stdexcept>

#include "engine_invocation.h"
#include "native_player.h"
#include "rakion_world_adapter.h"

extern "C" __declspec(dllimport) unsigned char CWorldBase_DLLClass;

namespace bot_engine
{
namespace
{
constexpr std::uintptr_t BattleTemplateLookupRva = 0x22b880;
constexpr std::uintptr_t GameInitializeRva = 0x1f2d0;
constexpr std::size_t GameInitializeVtableIndex = 51;

constexpr std::array<const wchar_t*, 6> ForbiddenGraphicsModules{
    L"d3d8.dll",
    L"d3d9.dll",
    L"d3d11.dll",
    L"ddraw.dll",
    L"dxgi.dll",
    L"opengl32.dll",
};

void ConfirmStaticEntityPackageImport()
{
    volatile unsigned char registrationByte = CWorldBase_DLLClass;
    static_cast<void>(registrationByte);
}
}

EngineRuntime::EngineRuntime(std::filesystem::path clientRoot)
    : clientRoot_(CanonicalPath(clientRoot)),
      binPath_(clientRoot_ / L"Bin")
{
}

EngineRuntime::~EngineRuntime()
{
    Shutdown();
}

EngineProbe EngineRuntime::Initialize()
{
    if (initialized_)
        throw std::logic_error("A engine já foi inicializada neste worker.");

    ConfirmStaticEntityPackageImport();
    ConfigureDllSearch();
    engine_ = LoadRequiredModule(L"engine.dll");

    auto* dedicated = reinterpret_cast<volatile LONG*>(
        ResolveRequired(DedicatedServerSymbol));
    InterlockedExchange(dedicated, 1);

    auto initEngine = reinterpret_cast<InitEngine>(
        ResolveRequired(InitEngineSymbol));
    endEngine_ = reinterpret_cast<EndEngine>(
        ResolveRequired(EndEngineSymbol));
    initEngine(CreateEngineString("Rakion"));
    initialized_ = true;
    auto enableStreams = reinterpret_cast<StreamHandling>(
        ResolveRequired(EnableStreamHandlingSymbol));
    enableStreams();
    streamsEnabled_ = true;
    worldAdapter_ = std::make_unique<RakionWorldAdapter>(engine_);
    worldAdapter_->Initialize();
    return Inspect();
}

WorldProbe EngineRuntime::LoadWorld(
    const std::string& worldName,
    std::uint8_t mapId,
    std::uint8_t mode)
{
    if (!initialized_)
        throw std::logic_error("A engine precisa ser inicializada primeiro.");
    if (worldLoaded_)
        throw std::logic_error("Este worker já possui um mundo carregado.");
    if (worldName.empty() || worldName.find("..") != std::string::npos)
        throw std::invalid_argument("Nome de mundo inválido.");
    if (mapId < 200 || mapId > 213 || mode < 1 || mode > 4)
        throw std::invalid_argument("Mapa ou modo battle inválido.");

    worldAdapter_->ConfigureField(mapId, mode);
    LoadGameplayModules();
    auto** network = reinterpret_cast<void**>(ResolveRequired(NetworkSymbol));
    if (!network || !*network)
        throw std::runtime_error("_pNetwork não foi inicializado.");

    auto startPeer = reinterpret_cast<StartPeerToPeer>(
        ResolveRequired(StartPeerToPeerSymbol));
    auto streamFilter = reinterpret_cast<StreamExceptionFilter>(
        ResolveRequired(StreamExceptionFilterSymbol));
    LegacyString session = CreateEngineString("OpenRakion Bot Engine");
    LegacyString world = CreateEngineString(worldName.c_str());
    std::array<unsigned char, 2048> sessionProperties{};

    bool loaded{};
    try
    {
        loaded = StartPeerWithStreamFaults(
            startPeer,
            streamFilter,
            *network,
            &session,
            &world,
            sessionProperties.data());
    }
    catch (...)
    {
        DestroyEngineString(world);
        DestroyEngineString(session);
        throw;
    }
    if (!loaded)
    {
        DestroyEngineString(world);
        DestroyEngineString(session);
        throw std::runtime_error(
            "A engine não conseguiu confirmar uma página do stream.");
    }
    using BattleTemplateLookup = void*(__cdecl*)(int, unsigned char);
    const auto lookup = reinterpret_cast<BattleTemplateLookup>(
        reinterpret_cast<std::uintptr_t>(entities_) +
        BattleTemplateLookupRva);
    if (!lookup(mode, mapId))
        throw std::runtime_error(
            "DataSetup não contém o template battle solicitado.");
    worldLoaded_ = true;
    DestroyEngineString(world);
    DestroyEngineString(session);
    return WorldProbe{Inspect(), true, worldName};
}

LocalPlayerProbe EngineRuntime::AddLocalPlayer(
    std::uint32_t botId,
    const std::string& name,
    const std::string& species)
{
    if (!worldLoaded_)
        throw std::logic_error("O mundo precisa estar carregado.");
    if (botId == 0 || name.empty() || species.empty())
        throw std::invalid_argument("Identidade do bot inválida.");
    if (botSources_.contains(botId))
        throw std::logic_error("O bot já existe neste worker.");
    if (LocalPlayerCount() >= LocalPlayerCapacity())
        throw std::runtime_error("A capacidade nativa de players foi atingida.");

    auto** network = reinterpret_cast<void**>(ResolveRequired(NetworkSymbol));
    worldAdapter_->SelectCharacter(botId, name);
    const auto result = CreateNativePlayer(
        engine_, *network, name, species);
    botSources_.emplace(botId, result.source);
    return {botId, result.activePlayers, result.capacity};
}

std::uint32_t EngineRuntime::LocalPlayerCount() const
{
    if (!initialized_)
        return 0;
    auto** network = reinterpret_cast<void**>(ResolveRequired(NetworkSymbol));
    if (!network || !*network)
        return 0;
    return GetNativePlayerCount(engine_, *network);
}

std::uint32_t EngineRuntime::LocalPlayerCapacity() const
{
    if (!initialized_)
        return 0;
    auto** network = reinterpret_cast<void**>(ResolveRequired(NetworkSymbol));
    if (!network || !*network)
        return 0;
    return GetNativePlayerCapacity(*network);
}

void EngineRuntime::ConfigureDllSearch()
{
    if (!std::filesystem::is_directory(binPath_))
        throw std::runtime_error("Diretório Bin do cliente não encontrado.");
    if (!PathsEqualInsensitive(ResolveExecutableClientRoot(), clientRoot_))
        throw std::runtime_error(
            "BotEngineHost.exe deve ser executado a partir do Bin do cliente.");

    if (!SetDllDirectoryW(binPath_.c_str()))
        throw std::runtime_error(
            "SetDllDirectoryW falhou: " + FormatWindowsError(GetLastError()));
    if (!SetCurrentDirectoryW(clientRoot_.c_str()))
        throw std::runtime_error(
            "SetCurrentDirectoryW falhou: " +
            FormatWindowsError(GetLastError()));
}

HMODULE EngineRuntime::LoadRequiredModule(const wchar_t* moduleName)
{
    if (HMODULE existing = GetModuleHandleW(moduleName))
        return existing;
    const auto modulePath = binPath_ / moduleName;
    HMODULE module = LoadLibraryW(modulePath.c_str());
    if (!module)
        throw std::runtime_error(
            "Falha ao carregar módulo obrigatório: " +
            modulePath.string() + ": " +
            FormatWindowsError(GetLastError()));
    return module;
}

FARPROC EngineRuntime::ResolveRequired(const char* symbol) const
{
    FARPROC address = engine_ ? GetProcAddress(engine_, symbol) : nullptr;
    if (!address)
        throw std::runtime_error(
            std::string("Export obrigatório ausente em engine.dll: ") +
            symbol);
    return address;
}

void EngineRuntime::LoadGameplayModules()
{
    if (game_)
        return;

    RegisterEntityPackage();
    if (!GetProcAddress(entities_, "CWorldBase_DLLClass"))
        throw std::runtime_error(
            "entitiesmp.dll não expõe CWorldBase_DLLClass.");

    gameModule_ = LoadRequiredModule(L"gamemp.dll");
    auto createGame = reinterpret_cast<CreateGame>(
        GetProcAddress(gameModule_, CreateGameSymbol));
    destroyGame_ = reinterpret_cast<DestroyGame>(
        GetProcAddress(gameModule_, DestroyGameSymbol));
    if (!createGame || !destroyGame_)
        throw std::runtime_error(
            "gamemp.dll não expõe GAME_Create/GAME_Destroy.");

    auto streamFilter = reinterpret_cast<StreamExceptionFilter>(
        ResolveRequired(StreamExceptionFilterSymbol));
    game_ = CreateGameWithStreamFaults(createGame, streamFilter);
    if (!game_)
        throw std::runtime_error("GAME_Create não criou CGame.");
    InitializeGameShell();
}

void EngineRuntime::InitializeGameShell()
{
    auto** vtable = *reinterpret_cast<void***>(game_);
    const auto initialize = reinterpret_cast<InitializeGame>(
        vtable[GameInitializeVtableIndex]);
    const auto expected = reinterpret_cast<void*>(
        reinterpret_cast<std::uintptr_t>(gameModule_) + GameInitializeRva);
    if (reinterpret_cast<void*>(initialize) != expected)
        throw std::runtime_error(
            "CGame::Initialize possui ABI incompatível.");

    auto constructFileName = reinterpret_cast<FileNameConstructor>(
        ResolveRequired(FileNameConstructorSymbol));
    auto destroyFileName = reinterpret_cast<FileNameDestructor>(
        ResolveRequired(FileNameDestructorSymbol));
    LegacyString path = CreateEngineString("Data\\SeriousSam.gms");
    LegacyString fileName{};
    constructFileName(&fileName, &path);
    try
    {
        auto streamFilter = reinterpret_cast<StreamExceptionFilter>(
            ResolveRequired(StreamExceptionFilterSymbol));
        if (!InitializeGameWithStreamFaults(
                initialize, streamFilter, game_, &fileName))
            throw std::runtime_error(
                "CGame::Initialize não concluiu o bootstrap de DataSetup.");
    }
    catch (...)
    {
        destroyFileName(&fileName);
        DestroyEngineString(path);
        throw;
    }
    destroyFileName(&fileName);
    DestroyEngineString(path);
}

void EngineRuntime::RegisterEntityPackage()
{
    auto getInstance = reinterpret_cast<GetEntitiesInstance>(
        ResolveRequired(EntitiesInstanceSymbol));
    auto loadPackage = reinterpret_cast<LoadEntitiesPackage>(
        ResolveRequired(EntitiesLoadSymbol));
    auto getHandle = reinterpret_cast<GetEntitiesHandle>(
        ResolveRequired(EntitiesHandleSymbol));
    void* instance = getInstance();
    if (!instance)
        throw std::runtime_error(
            "CEntitiesDLL::getInstance retornou nulo.");

    loadPackage(
        instance,
        CreateEngineString("Bin\\Entities.dll"));
    entities_ = getHandle(instance);
    if (!entities_)
        throw std::runtime_error(
            "CEntitiesDLL não registrou entitiesmp.dll.");
}

LegacyString EngineRuntime::CreateEngineString(const char* value) const
{
    auto constructor = reinterpret_cast<StringConstructor>(
        ResolveRequired(StringConstructorSymbol));
    LegacyString result{};
    constructor(&result, value);
    return result;
}

void EngineRuntime::DestroyEngineString(LegacyString& value) const noexcept
{
    if (!value.value)
        return;
    auto destructor = reinterpret_cast<StringDestructor>(
        GetProcAddress(engine_, "??1CTString@@QAE@XZ"));
    if (destructor)
        destructor(&value);
    value = {};
}

EngineProbe EngineRuntime::Inspect() const
{
    EngineProbe result{};
    result.engineLoaded = engine_ != nullptr;
    result.engineInitialized = initialized_;

    auto** network = reinterpret_cast<void**>(
        ResolveRequired(NetworkSymbol));
    auto** timer = reinterpret_cast<void**>(
        ResolveRequired(TimerSymbol));
    result.networkReady = network && *network;
    result.timerReady = timer && *timer;
    result.entitiesLoaded =
        GetModuleHandleW(L"entitiesmp.dll") != nullptr;

    for (const auto* module : ForbiddenGraphicsModules)
    {
        if (GetModuleHandleW(module))
            result.forbiddenModules.emplace_back(module);
    }
    return result;
}

void EngineRuntime::Shutdown() noexcept
{
    if (initialized_ && worldLoaded_ && engine_)
    {
        auto** network = reinterpret_cast<void**>(
            GetProcAddress(engine_, NetworkSymbol));
        auto stopGame = reinterpret_cast<StopGame>(
            GetProcAddress(engine_, StopGameSymbol));
        if (network && *network && stopGame)
        {
            __try
            {
                stopGame(*network);
            }
            __except (EXCEPTION_EXECUTE_HANDLER)
            {
            }
        }
    }
    if (game_ && destroyGame_)
    {
        __try
        {
            destroyGame_();
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
        }
    }
    worldAdapter_.reset();
    if (initialized_ && endEngine_)
    {
        __try
        {
            endEngine_();
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
        }
    }
    if (streamsEnabled_ && engine_)
    {
        auto disableStreams = reinterpret_cast<StreamHandling>(
            GetProcAddress(engine_, DisableStreamHandlingSymbol));
        if (disableStreams)
        {
            __try
            {
                disableStreams();
            }
            __except (EXCEPTION_EXECUTE_HANDLER)
            {
            }
        }
    }
    game_ = nullptr;
    destroyGame_ = nullptr;
    gameModule_ = nullptr;
    entities_ = nullptr;
    initialized_ = false;
    worldLoaded_ = false;
    streamsEnabled_ = false;
    endEngine_ = nullptr;
    engine_ = nullptr;
    botSources_.clear();
}

bool EngineRuntime::DumpEntitiesImage(
    const std::filesystem::path& destination) const
{
    if (!entities_)
        return false;
    const auto* base = reinterpret_cast<const std::uint8_t*>(entities_);
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE)
        return false;
    const auto* headers =
        reinterpret_cast<const IMAGE_NT_HEADERS32*>(base + dos->e_lfanew);
    if (headers->Signature != IMAGE_NT_SIGNATURE)
        return false;
    const std::size_t size = headers->OptionalHeader.SizeOfImage;
    std::ofstream output(destination, std::ios::binary | std::ios::trunc);
    if (!output)
        return false;
    output.write(reinterpret_cast<const char*>(base), size);
    return output.good();
}

}
