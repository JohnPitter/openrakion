namespace RakionServer.World.Network
{
    public static class FieldForceChangeTeamFrames
    {
        public static byte[] Changed(byte oldSeat, byte newSeat) =>
            new byte[] { 0, oldSeat, newSeat };

        public static byte[] Denied() => new byte[] { 2 };
    }
}
