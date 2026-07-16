namespace RakionServer.World.Domain
{
    public static class InventoryTimeRules
    {
        public static bool IsExpired(int limitTime, long nowMarker) =>
            limitTime > 0 && limitTime < nowMarker;
    }
}
