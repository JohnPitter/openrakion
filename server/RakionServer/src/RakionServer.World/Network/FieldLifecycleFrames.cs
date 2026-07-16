using System.Buffers.Binary;

namespace RakionServer.World.Network
{
    public static class FieldLifecycleFrames
    {
        public static byte[] Spawn(byte seat) => new[] { seat };

        public static byte[] SpawnRejected(byte seat, byte status) => new[] { seat, status };

        public static byte[] Exit(byte seat) => new[] { seat };

        public static byte[] ExitExperience(int remainingExperience)
        {
            var body = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(body, remainingExperience);
            return body;
        }

        public static byte[] Death(
            byte victimSeat, byte cause, byte killerSeat, byte scoreA, byte scoreB) =>
            new[] { victimSeat, cause, killerSeat, scoreA, scoreB };
    }
}
