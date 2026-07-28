using System;

namespace RakionServer.World.BotEngine;

internal static class BattleWorldCatalog
{
    private static readonly string[] Worlds =
    [
        @"LevelsSV\Icefield\Icefield.wld",
        @"LevelsSV\Tomb\Tomb.wld",
        @"LevelsSV\Draco\Draco.wld",
        @"LevelsSV\Snake\Snake.wld",
        @"LevelsSV\Altar\Altar.wld",
        @"LevelsSV\Covolt\Covolt.wld",
        @"LevelsSV\Castle\Castle.wld",
        @"LevelsSV\Lava\Lava.wld",
        @"LevelsSV\Lava2\Lava2.wld",
        @"LevelsSV\Cage\Cage.wld",
        @"LevelsSV\Gravity\Gravity.wld",
        @"LevelsSV\Mammoth\Mammoth.wld",
        @"LevelsSV\Underground\Underground.wld",
        @"LevelsSV\EightArenas\EightArenas.wld",
    ];

    public static bool Supports(byte mapId) =>
        mapId >= FirstBattleMapId && mapId - FirstBattleMapId < Worlds.Length;

    private const byte FirstBattleMapId = 200;

    public static string Resolve(byte mapId)
    {
        int index = mapId - FirstBattleMapId;
        if (index < 0 || index >= Worlds.Length)
            throw new ArgumentOutOfRangeException(
                nameof(mapId), mapId, "Mapa battle não suportado pelo Bot Engine Host.");
        return Worlds[index];
    }
}

internal sealed record BotEngineFieldRequest(
    uint FieldId,
    ushort MaximumBots,
    byte MapId,
    byte Mode,
    string WorldName);

internal sealed record BotEngineBotRequest(
    uint BotId,
    string Name,
    string Species);

internal readonly record struct BotEngineBot(
    uint BotId,
    uint ActivePlayers,
    uint Capacity);

internal readonly record struct BotEngineTick(
    uint FrameCount,
    uint ActivePlayers);

internal readonly record struct BotEnginePlayerSnapshot(
    uint BotId,
    bool Ready,
    bool Alive,
    float X,
    float Y,
    float Z,
    float RotationX,
    float RotationY,
    float RotationZ,
    float Hp);

[Flags]
internal enum BotEngineInput : uint
{
    None = 0,
    Forward = 1U << 0,
    Backward = 1U << 1,
    Left = 1U << 2,
    Right = 1U << 3,
    Jump = 1U << 4,
    PrimaryAttack = 1U << 5,
}

internal readonly record struct BotEngineInputResult(
    uint BotId,
    BotEngineInput Input);

internal readonly record struct BotEngineAim(
    uint BotId,
    float X,
    float Y,
    float Z);

internal enum BotEngineLifecycle : uint
{
    Alive = 1,
    Dead = 2,
}

internal readonly record struct BotEngineLifecycleResult(
    uint BotId,
    BotEngineLifecycle State);

internal readonly record struct BotEngineDamageReactionResult(
    uint BotId,
    byte AttackerSeat);
