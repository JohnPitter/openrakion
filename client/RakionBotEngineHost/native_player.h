#pragma once

#include <windows.h>

#include <cstdint>
#include <string>

namespace bot_engine
{
struct NativePlayerResult
{
    void* source{};
    std::uint32_t activePlayers{};
    std::uint32_t capacity{};
};

NativePlayerResult CreateNativePlayer(
    HMODULE engine,
    void* network,
    const std::string& name,
    const std::string& species);

std::uint32_t GetNativePlayerCount(HMODULE engine, void* network);
std::uint32_t GetNativePlayerCapacity(void* network);
}
