using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public readonly record struct GmFieldEntryRequest(ushort FieldId)
    {
        public static bool TryParse(byte[] payload, out GmFieldEntryRequest request)
        {
            request = default;
            if (payload.Length < 2) return false;
            request = new GmFieldEntryRequest(new PacketReader(payload).UInt16());
            return true;
        }
    }

    public static class GmFieldEntryFrames
    {
        public static byte[] Response(GmFieldEntrySnapshot snapshot)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x09)
                .WriteByte((byte)snapshot.Status)
                .WriteWord(snapshot.FieldId);
            if (snapshot.Status == GmFieldEntryStatus.Success)
                writer.WriteCString(snapshot.RoomName)
                    .WriteCString(snapshot.CreatorCharacter);
            return writer.ToArray();
        }
    }
}
