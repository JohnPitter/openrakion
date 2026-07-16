using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public static class RoomRosterFrames
    {
        public static byte[] SnapshotBody(Field field)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(field.Id)
                .WriteByte(field.State)
                .WriteByte(field.MasterSlot < 0 ? 0x14 : field.MasterSlot)
                .WriteByte(field.MapId)
                .WriteByte(field.Mode)
                .WriteByte(field.MinLevel)
                .WriteByte(field.MaxLevel)
                .WriteByte(0)
                .WriteByte(field.Round)
                .WriteByte(field.MaxRounds)
                .WriteWord(field.RoundDurationSec)
                .WriteByte(field.FragLimit)
                .WriteCString(field.Name)
                .WriteCString(field.Password)
                .WriteCString(field.Description);

            foreach (PlayerRec record in field.Slots)
            {
                writer.WriteByte(record.State);
                if (!record.Occupied || record.Session == null) continue;
                writer.WriteWord(record.Session.Slot).WriteByte(0);
                record.Session.WriteRoomPlayerRecord(writer, record.UsesTunneling);
            }
            return writer.ToArray();
        }

        public static byte[] PlayerJoined(PlayerRec record)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x38)
                .WriteByte(0)
                .WriteByte(record.Slot)
                .WriteByte(record.State);
            if (record.Occupied && record.Session != null)
            {
                writer.WriteWord(record.Session.Slot).WriteByte(0);
                record.Session.WriteRoomPlayerRecord(writer, record.UsesTunneling);
            }
            return writer.ToArray();
        }
    }
}
