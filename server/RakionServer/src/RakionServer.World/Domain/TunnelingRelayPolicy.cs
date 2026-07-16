namespace RakionServer.World.Domain
{
    public static class TunnelingRelayPolicy
    {
        public static bool ShouldRelayAll(
            Field field, PlayerRec sender, PlayerRec target) =>
            sender != target && ShouldRelay(field, sender, target);

        public static bool ShouldRelayOne(
            Field field, PlayerRec sender, PlayerRec target) =>
            ShouldRelay(field, sender, target);

        private static bool ShouldRelay(
            Field field, PlayerRec sender, PlayerRec target) =>
            field.State == 2 &&
            field.HasTunnelingClient &&
            sender.Playing &&
            target.Playing &&
            (sender.UsesTunneling || target.UsesTunneling);
    }
}
