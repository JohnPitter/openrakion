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

struct NativePlayerSnapshot
{
    std::uint32_t botId{};
    bool ready{};
    bool alive{};
    float position[3]{};
    float rotation[3]{};
    float hp{};
};

enum NativeInputFlag : std::uint32_t
{
    InputForward = 1U << 0,
    InputBackward = 1U << 1,
    InputLeft = 1U << 2,
    InputRight = 1U << 3,
    InputJump = 1U << 4,
    InputPrimaryAttack = 1U << 5,
    InputMask = (1U << 6) - 1,
};

NativePlayerResult CreateNativePlayer(
    HMODULE engine,
    void* network,
    const std::string& name,
    const std::string& species);

std::uint32_t GetNativePlayerCount(HMODULE engine, void* network);
std::uint32_t GetNativePlayerCapacity(void* network);
NativePlayerSnapshot InspectNativePlayer(
    HMODULE engine,
    HMODULE entities,
    void* network,
    void* source,
    std::uint32_t botId);
void ApplyNativeInput(
    HMODULE engine,
    void* source,
    std::uint32_t inputFlags);
void AimNativePlayer(
    HMODULE engine,
    void* network,
    void* source,
    const float* target);
void SetNativeLifecycle(
    HMODULE engine,
    HMODULE entities,
    void* network,
    void* source,
    bool alive);
}
