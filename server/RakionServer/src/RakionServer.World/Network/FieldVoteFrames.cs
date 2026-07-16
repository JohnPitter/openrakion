using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public static class FieldVoteFrames
    {
        public static byte[] OpenBody(byte targetSeat, string reason)
        {
            using var writer = new PacketWriter();
            writer.WriteByte(targetSeat).WriteCString(reason);
            return writer.ToArray();
        }

        public static byte[] ResultBody(FieldVoteFinal result) => new[]
        {
            (byte)0, result.Result, result.Eligible, result.Yes,
            result.No, result.Abstain, result.TargetSeat
        };

        public static byte[] Status(FieldVoteStatus status)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x5f).WriteByte((byte)status);
            return writer.ToArray();
        }
    }
}
