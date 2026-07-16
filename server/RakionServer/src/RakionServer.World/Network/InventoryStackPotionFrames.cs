using System.Collections.Generic;
using RakionServer.Common;

namespace RakionServer.World.Network
{
    public static class InventoryStackPotionFrames
    {
        public static byte[] Error(byte status)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x73).WriteByte(status);
            return writer.ToArray();
        }

        public static byte[] SuccessBody(
            int gameInfoId, int characterId, byte source, byte destination,
            IReadOnlyList<int> boxItems)
        {
            using var writer = new PacketWriter();
            writer.WriteInt32(gameInfoId).WriteByte(source).WriteByte(destination);
            writer.WriteUInt32(0).WriteUInt32(0).WriteUInt32(0).WriteInt32(characterId);
            writer.WriteByte(0);
            WriteBoxSnapshot(writer, boxItems);
            writer.WriteByte(0);
            return writer.ToArray();
        }

        private static void WriteBoxSnapshot(PacketWriter writer, IReadOnlyList<int> boxItems)
        {
            byte count = 0;
            for (int index = 0; index < boxItems.Count && index < 0x78; index++)
                if (boxItems[index] != 0) count++;
            writer.WriteByte(count);
            for (int index = 0; index < boxItems.Count && index < 0x78; index++)
                if (boxItems[index] != 0) writer.WriteUInt32((uint)boxItems[index]);
            for (int index = 0; index < boxItems.Count && index < 0x78; index++)
                if (boxItems[index] != 0) writer.WriteByte((byte)index);
        }
    }
}
