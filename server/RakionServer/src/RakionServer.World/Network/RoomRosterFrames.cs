using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public static class RoomRosterFrames
    {
        private const int SyntheticBotPort = 1183;

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
                if (record.Bot != null) { writer.WriteWord(BotWireSlot(record)).WriteByte(0); WriteBotRecord(writer, record.Bot); continue; }
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
            if (record.Bot != null)
            {
                writer.WriteWord(BotWireSlot(record)).WriteByte(0);
                WriteBotRecord(writer, record.Bot);
            }
            else if (record.Occupied && record.Session != null)
            {
                writer.WriteWord(record.Session.Slot).WriteByte(0);
                record.Session.WriteRoomPlayerRecord(writer, record.UsesTunneling);
            }
            return writer.ToArray();
        }

        /// <summary>Slot "de rede" sintético do bot (base alta p/ não colidir com slots de sessão real).</summary>
        private static ushort BotWireSlot(PlayerRec record) => (ushort)(0x0400 + record.Slot);

        /// <summary>
        /// Registro do bot no roster, no MESMO layout de <c>WriteRoomPlayerRecord</c>: nome, buddy vazio,
        /// sem tunneling, endpoint loopback marcado (a DLL reconhece a entidade sintética pela porta),
        /// classe/level e quickslots vazios.
        /// </summary>
        private static void WriteBotRecord(PacketWriter writer, BotPlayer bot)
        {
            writer.WriteCString(bot.Name)
                .WriteCString("")
                .WriteByte(0)          // usesTunneling
                .WriteInt32(0);        // groupId
            NetworkEndpointCodec.WritePort(writer, SyntheticBotPort);
            writer.WriteBytes(new byte[] { 127, 0, 0, 1 });
            NetworkEndpointCodec.WritePort(writer, SyntheticBotPort);
            writer.WriteByte(bot.CharClass)
                .WriteByte(bot.Level)
                .WriteByte(0);
            for (int slot = 0; slot < 0x13; slot++) writer.WriteWord(0);   // quickslots vazios
            for (int slot = 0; slot < 0x13; slot++) writer.WriteByte(0);   // níveis vazios
        }
    }
}
