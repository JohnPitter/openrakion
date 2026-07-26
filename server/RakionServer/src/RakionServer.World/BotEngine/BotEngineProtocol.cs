using System;

namespace RakionServer.World.BotEngine;

internal static class BotEngineProtocol
{
    public const uint Magic = 0x4842524F;
    public const ushort Version = 4;
    public const ushort ResponseFlag = 0x8000;
    public const int HeaderSize = 20;
    public const int MaximumPayloadSize = 4096;
    public const int WorldNameCapacity = 260;
    public const int LoadFieldRequestSize = 268;
    public const int AddBotRequestSize = 52;
    public const int SnapshotResponseSize = 36;

    [Flags]
    public enum Capability : uint
    {
        EngineBootstrap = 1U << 0,
        NativeWorld = 1U << 1,
        NativePlayerSources = 1U << 2,
        NativeSnapshots = 1U << 3,
        NativeInputs = 1U << 4,
    }

    public enum MessageType : ushort
    {
        Hello = 1,
        LoadField = 2,
        Ping = 3,
        Shutdown = 4,
        AddBot = 5,
        Tick = 6,
        Snapshot = 7,
        Input = 8,
    }

    public enum Status : uint
    {
        Success = 0,
        InvalidFrame = 1,
        UnsupportedVersion = 2,
        InvalidState = 3,
        EngineFailure = 4,
        BadRequest = 5,
        UnsupportedMessage = 6,
    }
}

internal readonly record struct BotEngineFrame(
    BotEngineProtocol.MessageType Type,
    uint CorrelationId,
    BotEngineProtocol.Status Status,
    byte[] Payload);

internal readonly record struct BotEngineHello(
    uint ProcessId,
    BotEngineProtocol.Capability Capabilities,
    ushort ProtocolVersion);

internal readonly record struct BotEngineHealth(
    ulong MonotonicMilliseconds,
    uint FieldId,
    uint BotCount);
