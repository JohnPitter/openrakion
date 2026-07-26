#include "native_player.h"

#include <algorithm>
#include <array>
#include <cstdio>
#include <stdexcept>

#include "engine_abi.h"

namespace bot_engine
{
namespace
{
constexpr std::size_t PlayerCharacterSize = 0x44;
constexpr std::size_t LocalSourcesOffset = 0x28;
constexpr std::size_t SourceActionOffset = 0x58;
constexpr std::size_t ButtonsOffset = 0x10;
constexpr std::size_t StrafeAxisOffset = 0x38;
constexpr std::size_t ForwardAxisOffset = 0x40;
constexpr std::size_t ActionStateOffset = 0x44;
constexpr std::uint32_t PrimaryAttackButton = 0x00000001;
constexpr std::uint32_t JumpButton = 0x00000004;
constexpr std::uint32_t MoveForwardButton = 0x00000020;
constexpr std::uint32_t MoveBackwardButton = 0x00100000;
constexpr std::uint32_t MoveLeftButton = 0x08000000;
constexpr std::uint32_t MoveRightButton = 0x10000000;
constexpr std::uint32_t NavigationButtons =
    PrimaryAttackButton | JumpButton | MoveForwardButton |
    MoveBackwardButton | MoveLeftButton | MoveRightButton;
constexpr float MovementAxis = 6.0f;

struct EngineFault
{
    DWORD code{};
    const void* address{};
    const void* access{};
    const void* caller{};
};

EngineFault LastFault{};

FARPROC Resolve(HMODULE engine, const char* symbol)
{
    FARPROC address = GetProcAddress(engine, symbol);
    if (!address)
        throw std::runtime_error(
            std::string("Export de player ausente: ") + symbol);
    return address;
}

int RecordFault(EXCEPTION_POINTERS* exception)
{
    LastFault.code = exception->ExceptionRecord->ExceptionCode;
    LastFault.address = exception->ExceptionRecord->ExceptionAddress;
    LastFault.access = reinterpret_cast<const void*>(
        exception->ExceptionRecord->ExceptionInformation[1]);
    LastFault.caller = *reinterpret_cast<const void* const*>(
        exception->ContextRecord->Esp + sizeof(void*));
    return EXCEPTION_EXECUTE_HANDLER;
}

int FilterFault(
    StreamExceptionFilter filter,
    DWORD code,
    EXCEPTION_POINTERS* exception)
{
    const int decision = filter(code, exception);
    if (decision == EXCEPTION_EXECUTE_HANDLER)
        RecordFault(exception);
    return decision;
}

void* InvokeAddPlayer(
    AddPlayer addPlayer,
    StreamExceptionFilter filter,
    void* network,
    void* character)
{
    __try
    {
        return addPlayer(network, character);
    }
    __except (FilterFault(
        filter,
        GetExceptionCode(),
        GetExceptionInformation()))
    {
        return nullptr;
    }
}

void* InvokeAddPlayerSafely(
    AddPlayer addPlayer,
    StreamExceptionFilter filter,
    void* network,
    void* character)
{
    __try
    {
        return InvokeAddPlayer(addPlayer, filter, network, character);
    }
    __except (RecordFault(GetExceptionInformation()))
    {
        return nullptr;
    }
}

LegacyString CreateString(
    StringConstructor constructor,
    const char* value)
{
    LegacyString result{};
    constructor(&result, value);
    return result;
}

std::runtime_error CreateFailure()
{
    char message[256]{};
    std::snprintf(
        message,
        sizeof(message),
        "AddPlayer_t falhou: seh=0x%08lX address=%p access=%p caller=%p.",
        static_cast<unsigned long>(LastFault.code),
        LastFault.address,
        LastFault.access,
        LastFault.caller);
    return std::runtime_error(message);
}

bool InvokeInputSafely(
    ApplyAction applyAction,
    SendAction sendAction,
    void* source)
{
    __try
    {
        int applied{};
        applyAction(source, applied);
        sendAction(source);
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}
}

NativePlayerResult CreateNativePlayer(
    HMODULE engine,
    void* network,
    const std::string& name,
    const std::string& species)
{
    auto constructString = reinterpret_cast<StringConstructor>(
        Resolve(engine, StringConstructorSymbol));
    auto destroyString = reinterpret_cast<StringDestructor>(
        Resolve(engine, StringDestructorSymbol));
    auto constructPlayer = reinterpret_cast<PlayerConstructor>(
        Resolve(engine, PlayerConstructorSymbol));
    auto destroyPlayer = reinterpret_cast<PlayerDestructor>(
        Resolve(engine, PlayerDestructorSymbol));
    auto addPlayer = reinterpret_cast<AddPlayer>(
        Resolve(engine, AddPlayerSymbol));
    auto streamFilter = reinterpret_cast<StreamExceptionFilter>(
        Resolve(engine, StreamExceptionFilterSymbol));

    alignas(16) std::array<std::uint8_t, PlayerCharacterSize> character{};
    LegacyString playerName = CreateString(constructString, name.c_str());
    LegacyString playerSpecies = CreateString(
        constructString, species.c_str());
    void* source{};
    bool playerConstructed{};
    try
    {
        constructPlayer(character.data(), &playerName, &playerSpecies);
        playerConstructed = true;
        source = InvokeAddPlayerSafely(
            addPlayer, streamFilter, network, character.data());
    }
    catch (...)
    {
        if (playerConstructed)
            destroyPlayer(character.data());
        destroyString(&playerSpecies);
        destroyString(&playerName);
        throw;
    }
    destroyPlayer(character.data());
    destroyString(&playerSpecies);
    destroyString(&playerName);
    if (!source)
        throw CreateFailure();
    return {
        source,
        GetNativePlayerCount(engine, network),
        GetNativePlayerCapacity(network),
    };
}

std::uint32_t GetNativePlayerCount(HMODULE engine, void* network)
{
    if (!network)
        return 0;
    auto getCount = reinterpret_cast<GetLocalPlayerCount>(
        Resolve(engine, GetLocalPlayerCountSymbol));
    const long count = getCount(network);
    return count > 0 ? static_cast<std::uint32_t>(count) : 0;
}

std::uint32_t GetNativePlayerCapacity(void* network)
{
    if (!network)
        return 0;
    const auto* bytes = static_cast<const std::uint8_t*>(network);
    const long capacity = *reinterpret_cast<const long*>(
        bytes + LocalSourcesOffset);
    return capacity > 0 ? static_cast<std::uint32_t>(capacity) : 0;
}

NativePlayerSnapshot InspectNativePlayer(
    HMODULE engine,
    HMODULE entities,
    void* network,
    void* source,
    std::uint32_t botId)
{
    NativePlayerSnapshot snapshot{botId};
    if (!engine || !entities || !network || !source)
        return snapshot;

    auto getEntity = reinterpret_cast<GetLocalPlayerEntity>(
        Resolve(engine, GetLocalPlayerEntitySymbol));
    auto getPlacement = reinterpret_cast<GetPlacement>(
        Resolve(engine, GetPlacementSymbol));
    auto isAlive = reinterpret_cast<IsAlive>(
        GetProcAddress(entities, IsAliveSymbol));
    auto getHp = reinterpret_cast<GetHp>(
        GetProcAddress(entities, GetHpSymbol));
    if (!isAlive || !getHp)
        throw std::runtime_error("Exports de snapshot do player ausentes.");

    void* entity = getEntity(network, source);
    const float* placement = entity ? getPlacement(entity) : nullptr;
    if (!entity || !placement)
        return snapshot;

    snapshot.ready = true;
    snapshot.alive = isAlive(entity) != 0;
    for (std::size_t index = 0; index < 3; ++index)
    {
        snapshot.position[index] = placement[index];
        snapshot.rotation[index] = placement[index + 3];
    }
    snapshot.hp = getHp(entity);
    return snapshot;
}

void ApplyNativeInput(
    HMODULE engine,
    void* source,
    std::uint32_t inputFlags)
{
    const bool conflictingForward =
        (inputFlags & (InputForward | InputBackward)) ==
        (InputForward | InputBackward);
    const bool conflictingStrafe =
        (inputFlags & (InputLeft | InputRight)) ==
        (InputLeft | InputRight);
    if (!source || (inputFlags & ~InputMask) != 0 ||
        conflictingForward || conflictingStrafe)
        throw std::invalid_argument("Input nativo inválido.");

    auto* action = static_cast<std::uint8_t*>(source) + SourceActionOffset;
    auto& buttons = *reinterpret_cast<std::uint32_t*>(
        action + ButtonsOffset);
    buttons &= ~NavigationButtons;
    auto& strafe = *reinterpret_cast<float*>(action + StrafeAxisOffset);
    auto& forward = *reinterpret_cast<float*>(action + ForwardAxisOffset);
    strafe = 0.0f;
    forward = 0.0f;
    action[ActionStateOffset] =
        (inputFlags & InputPrimaryAttack) != 0 ? 1 : 0;

    if ((inputFlags & InputForward) != 0)
    {
        buttons |= MoveForwardButton;
        forward = -MovementAxis;
    }
    if ((inputFlags & InputBackward) != 0)
    {
        buttons |= MoveBackwardButton;
        forward = MovementAxis;
    }
    if ((inputFlags & InputLeft) != 0)
    {
        buttons |= MoveLeftButton;
        strafe = -MovementAxis;
    }
    if ((inputFlags & InputRight) != 0)
    {
        buttons |= MoveRightButton;
        strafe = MovementAxis;
    }
    if ((inputFlags & InputJump) != 0)
        buttons |= JumpButton;
    if ((inputFlags & InputPrimaryAttack) != 0)
        buttons |= PrimaryAttackButton;

    auto applyAction = reinterpret_cast<ApplyAction>(
        Resolve(engine, ApplyActionSymbol));
    auto sendAction = reinterpret_cast<SendAction>(
        Resolve(engine, SendActionSymbol));
    if (!InvokeInputSafely(applyAction, sendAction, source))
        throw std::runtime_error("CPlayerSource recusou o input.");
}

void AimNativePlayer(
    HMODULE engine,
    void* network,
    void* source,
    const float* target)
{
    if (!network || !source || !target)
        throw std::invalid_argument("Alvo nativo inválido.");
    auto getEntity = reinterpret_cast<GetLocalPlayerEntity>(
        Resolve(engine, GetLocalPlayerEntitySymbol));
    auto getPlacement = reinterpret_cast<GetPlacement>(
        Resolve(engine, GetPlacementSymbol));
    auto setPlacement = reinterpret_cast<SetPlacement>(
        Resolve(engine, SetPlacementSymbol));
    auto toAngles = reinterpret_cast<DirectionVectorToAngles>(
        Resolve(engine, DirectionVectorToAnglesSymbol));
    void* entity = getEntity(network, source);
    const float* placement = entity ? getPlacement(entity) : nullptr;
    if (!placement)
        throw std::runtime_error("Entidade nativa ainda não está pronta.");

    const float direction[3]{
        target[0] - placement[0],
        0.0f,
        target[2] - placement[2],
    };
    if (direction[0] * direction[0] + direction[2] * direction[2] < 0.0001f)
        return;
    float updated[6]{};
    std::copy_n(placement, 6, updated);
    __try
    {
        toAngles(direction, updated + 3);
        setPlacement(entity, updated);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        throw std::runtime_error("A engine recusou a orientação nativa.");
    }
}
}
