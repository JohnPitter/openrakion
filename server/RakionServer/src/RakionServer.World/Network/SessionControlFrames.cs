using RakionServer.Common;

namespace RakionServer.World.Network
{
    public static class SessionControlFrames
    {
        public static byte[] Disconnect(int connectionLogId, ushort reason, int gameInfoId)
        {
            using var writer = new PacketWriter();
            return writer.WriteInt32(connectionLogId)
                .WriteWord(reason)
                .WriteInt32(gameInfoId)
                .ToArray();
        }
    }
}
