using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public static class WhisperLocationProtocol
    {
        public static byte[] Whisper(string sender, string text)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x16).WriteByte(0).WriteCString(sender).WriteCString(text);
            return writer.ToArray();
        }

        public static byte[] WhisperNotFound()
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x16).WriteByte(1);
            return writer.ToArray();
        }

        public static byte[] WhereAmI(byte serverId, WorldLocation location)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x17).WriteByte(serverId).WriteByte(location.Kind).WriteWord(location.Id);
            return writer.ToArray();
        }

        public static byte[] WhereAreYou(string name, WorldLocation location)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x18).WriteByte(0).WriteCString(name)
                .WriteByte(location.Kind).WriteWord(location.Id);
            return writer.ToArray();
        }

        public static byte[] WhereAreYouNotFound(string name)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x18).WriteByte(1).WriteCString(name);
            return writer.ToArray();
        }
    }
}
