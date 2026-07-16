using RakionServer.Common;

namespace RakionServer.World.Network
{
    public static class InventoryExpirationFrames
    {
        public static byte[] ActiveSlotClear(byte activeSlot, int emptyBoxSlot)
        {
            bool hasEmptyBoxSlot = emptyBoxSlot is >= 0 and < 120;
            using var writer = new PacketWriter();
            writer.WriteWord(0x31);
            writer.WriteByte(0);
            writer.WriteByte(1);
            writer.WriteByte(activeSlot);
            writer.WriteWord(0);
            writer.WriteByte(1);
            writer.WriteUInt32(0);
            writer.WriteByte(hasEmptyBoxSlot ? (byte)0 : (byte)1);
            writer.WriteByte(hasEmptyBoxSlot ? checked((byte)emptyBoxSlot) : activeSlot);
            writer.WriteWord(0);
            writer.WriteByte(1);
            writer.WriteUInt32(0);
            return writer.ToArray();
        }
    }
}
