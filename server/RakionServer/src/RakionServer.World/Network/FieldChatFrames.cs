using RakionServer.Common;

namespace RakionServer.World.Network
{
    public static class FieldChatFrames
    {
        public static byte[] Message(byte senderSeat, string text)
        {
            using var writer = new PacketWriter();
            writer.WriteByte(senderSeat).WriteCString(text);
            return writer.ToArray();
        }
    }
}
