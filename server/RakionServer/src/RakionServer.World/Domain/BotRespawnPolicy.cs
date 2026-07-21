namespace RakionServer.World.Domain
{
    public static class BotRespawnPolicy
    {
        public const int CompetitiveDelayMs = 7000;

        public static int DelayMs(byte mode) => mode is
            (byte)GameMode.Deathmatch or
            (byte)GameMode.TeamDeath or
            (byte)GameMode.Boss
                ? CompetitiveDelayMs
                : 0;
    }
}
