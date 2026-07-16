using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public static class FieldInvitationFrames
    {
        public static byte[] Notification(ushort inviterSlot, string inviterName, ushort fieldRef, Field field)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x72)
                .WriteWord(inviterSlot)
                .WriteCString(inviterName)
                .WriteWord(fieldRef)
                .WriteByte(field.MapId)
                .WriteByte(field.Mode)
                .WriteByte(field.MinLevel)
                .WriteByte(field.MaxLevel)
                .WriteByte(field.LevelRangeCode)
                .WriteByte(field.MaxRounds)
                .WriteWord(field.RoundDurationSec)
                .WriteCString(field.Name)
                .WriteCString(field.Description);
            return writer.ToArray();
        }
    }
}
