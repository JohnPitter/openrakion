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

    public static string Resolve(byte mapId)
    {
        if (mapId >= Worlds.Length)
            throw new ArgumentOutOfRangeException(
                nameof(mapId), mapId, "Mapa battle não suportado pelo Bot Engine Host.");
        return Worlds[mapId];
    }
}

internal sealed record BotEngineFieldRequest(
    uint FieldId,
    ushort MaximumBots,
    string WorldName);
