namespace RakionServer.World.Domain
{
    public readonly record struct GamePointLimit(uint MaxExp, uint MaxGold);

    public static class GamePointRules
    {
        public static GamePointLimit GetLimit(byte mode, byte round, byte maxRounds) => mode switch
        {
            0 => new GamePointLimit(1500, 500),
            (byte)GameMode.Golem when round == maxRounds => new GamePointLimit(80, 200),
            (byte)GameMode.Golem => new GamePointLimit(100, 100),
            (byte)GameMode.Deathmatch or (byte)GameMode.Boss => new GamePointLimit(115, 160),
            (byte)GameMode.TeamDeath => new GamePointLimit(90, 70),
            _ => default
        };

        public static bool IsValid(byte mode, byte round, byte maxRounds, uint exp, uint gold)
        {
            GamePointLimit limit = GetLimit(mode, round, maxRounds);
            return exp <= limit.MaxExp && gold <= limit.MaxGold;
        }
    }
}
