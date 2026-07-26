#pragma once

#include <cstddef>
#include <cstdint>

namespace bot_engine::protocol
{
constexpr std::uint32_t Magic = 0x4842524F;
constexpr std::uint16_t Version = 4;
constexpr std::uint16_t ResponseFlag = 0x8000;
constexpr std::uint32_t MaximumPayloadSize = 4096;
constexpr std::size_t WorldNameCapacity = 260;
constexpr std::size_t PlayerNameCapacity = 32;
constexpr std::size_t SpeciesCapacity = 16;

enum class MessageType : std::uint16_t
{
    Hello = 1,
    LoadField = 2,
    Ping = 3,
    Shutdown = 4,
    AddBot = 5,
    Tick = 6,
    Snapshot = 7,
    Input = 8,
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
    NativePlayerSources = 1U << 2,
    NativeSnapshots = 1U << 3,
    NativeInputs = 1U << 4,
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
    std::uint8_t mapId;
    std::uint8_t mode;
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

struct AddBotRequest
{
    std::uint32_t botId;
    char name[PlayerNameCapacity];
    char species[SpeciesCapacity];
};

struct AddBotResponse
{
    std::uint32_t botId;
    std::uint32_t activePlayers;
    std::uint32_t capacity;
};

struct TickRequest
{
    std::uint32_t frameCount;
};

struct TickResponse
{
    std::uint32_t frameCount;
    std::uint32_t activePlayers;
};

struct SnapshotRequest
{
    std::uint32_t botId;
};

enum SnapshotFlag : std::uint32_t
{
    SnapshotReady = 1U << 0,
    SnapshotAlive = 1U << 1,
};

struct SnapshotResponse
{
    std::uint32_t botId;
    std::uint32_t flags;
    float position[3];
    float rotation[3];
    float hp;
};

struct InputRequest
{
    std::uint32_t botId;
    std::uint32_t flags;
};

struct InputResponse
{
    std::uint32_t botId;
    std::uint32_t flags;
};
#pragma pack(pop)

static_assert(sizeof(FrameHeader) == 20);
static_assert(sizeof(HelloResponse) == 12);
static_assert(sizeof(LoadFieldRequest) == 268);
static_assert(sizeof(LoadFieldResponse) == 8);
static_assert(sizeof(PingResponse) == 16);
static_assert(sizeof(AddBotRequest) == 52);
static_assert(sizeof(AddBotResponse) == 12);
static_assert(sizeof(TickRequest) == 4);
static_assert(sizeof(TickResponse) == 8);
static_assert(sizeof(SnapshotRequest) == 4);
static_assert(sizeof(SnapshotResponse) == 36);
static_assert(sizeof(InputRequest) == 8);
static_assert(sizeof(InputResponse) == 8);
}
