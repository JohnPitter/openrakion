#pragma once

#include <cstddef>
#include <cstdint>

namespace bot_engine::protocol
{
constexpr std::uint32_t Magic = 0x4842524F;
constexpr std::uint16_t Version = 1;
constexpr std::uint16_t ResponseFlag = 0x8000;
constexpr std::uint32_t MaximumPayloadSize = 4096;
constexpr std::size_t WorldNameCapacity = 260;

enum class MessageType : std::uint16_t
{
    Hello = 1,
    LoadField = 2,
    Ping = 3,
    Shutdown = 4,
};

enum class Status : std::uint32_t
{
    Success = 0,
    InvalidFrame = 1,
    UnsupportedVersion = 2,
    InvalidState = 3,
    EngineFailure = 4,
    BadRequest = 5,
    UnsupportedMessage = 6,
};

enum Capability : std::uint32_t
{
    EngineBootstrap = 1U << 0,
    NativeWorld = 1U << 1,
};

#pragma pack(push, 1)
struct FrameHeader
{
    std::uint32_t magic;
    std::uint16_t version;
    std::uint16_t messageType;
    std::uint32_t payloadSize;
    std::uint32_t correlationId;
    std::uint32_t status;
};

struct HelloResponse
{
    std::uint32_t processId;
    std::uint32_t capabilities;
    std::uint16_t protocolVersion;
    std::uint16_t reserved;
};

struct LoadFieldRequest
{
    std::uint32_t fieldId;
    std::uint16_t maximumBots;
    std::uint16_t reserved;
    char worldName[WorldNameCapacity];
};

struct LoadFieldResponse
{
    std::uint32_t fieldId;
    std::uint32_t maximumBots;
};

struct PingResponse
{
    std::uint64_t monotonicMilliseconds;
    std::uint32_t fieldId;
    std::uint32_t botCount;
};
#pragma pack(pop)

static_assert(sizeof(FrameHeader) == 20);
static_assert(sizeof(HelloResponse) == 12);
static_assert(sizeof(LoadFieldRequest) == 268);
static_assert(sizeof(LoadFieldResponse) == 8);
static_assert(sizeof(PingResponse) == 16);
}
