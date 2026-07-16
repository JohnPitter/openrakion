namespace RakionServer.World.Domain
{
    public static class KeepAliveRules
    {
        public static bool IsLate(long elapsedMilliseconds) => elapsedMilliseconds > 90_000;
    }
}
