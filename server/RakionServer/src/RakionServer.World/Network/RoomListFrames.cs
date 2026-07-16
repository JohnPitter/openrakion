using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public static class RoomListFrames
    {
        public static void WriteEntry(PacketWriter writer, RoomListSnapshot field)
        {
            writer.WriteWord(field.FieldId)
                .WriteByte(field.HasPassword ? (byte)1 : (byte)0)
                .WriteByte(field.InGame ? (byte)1 : (byte)0)
                .WriteByte(field.MapId)
                .WriteByte(field.Mode)
                .WriteByte(field.MinLevel)
                .WriteByte(field.MaxLevel)
                .WriteByte(field.LevelRangeCode)
                .WriteByte(field.Round)
                .WriteByte(field.MaxRounds)
                .WriteByte(field.PlayerCount)
                .WriteByte(field.MaxPlayers)
                .WriteInt32(field.MasterUserId)
                .WriteWord(field.MasterSeat)
                .WriteInt32(0)
                .WriteWord(0)
                .WriteCString(field.Name)
                .WriteWord(field.Marker);
        }
    }
}
