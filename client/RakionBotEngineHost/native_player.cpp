#include "native_player.h"

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
}
