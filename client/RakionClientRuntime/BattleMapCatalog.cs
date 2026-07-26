namespace RakionClientRuntime;

public static class BattleMapCatalog
{
    private static readonly IReadOnlyDictionary<string, byte> Maps =
        new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            [@"LevelsSV\Icefield\Icefield.wld"] = 200,
            [@"LevelsSV\Tomb\Tomb.wld"] = 201,
            [@"LevelsSV\Draco\Draco.wld"] = 202,
            [@"LevelsSV\Snake\Snake.wld"] = 203,
            [@"LevelsSV\Altar\Altar.wld"] = 204,
            [@"LevelsSV\Covolt\Covolt.wld"] = 205,
            [@"LevelsSV\Castle\Castle.wld"] = 206,
            [@"LevelsSV\Lava\Lava.wld"] = 207,
            [@"LevelsSV\Lava2\Lava2.wld"] = 208,
            [@"LevelsSV\Cage\Cage.wld"] = 209,
            [@"LevelsSV\Gravity\Gravity.wld"] = 210,
            [@"LevelsSV\Mammoth\Mammoth.wld"] = 211,
            [@"LevelsSV\Underground\Underground.wld"] = 212,
            [@"LevelsSV\EightArenas\EightArenas.wld"] = 213
        };

    public static byte Resolve(string worldName)
    {
        string normalized = worldName.Replace('/', '\\');
        if (Maps.TryGetValue(normalized, out byte mapId)) return mapId;
        throw new ArgumentException(
            $"World battle não suportado pelo host headless: {worldName}.");
    }
}
