using RakionServer.Common;

namespace RakionServer.World.Network
{
    public readonly record struct InventoryMoveCell(
        byte Type, byte Slot, ushort ItemId, byte Metadata, uint Value);

    public static class InventoryMoveFrames
    {
        public static byte[] Response(
            byte status, InventoryMoveCell source, InventoryMoveCell destination)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x31).WriteByte(status);
            WriteCell(writer, source);
            WriteCell(writer, destination);
            return writer.ToArray();
        }

        private static void WriteCell(PacketWriter writer, InventoryMoveCell cell) =>
            writer.WriteByte(cell.Type).WriteByte(cell.Slot).WriteWord(cell.ItemId)
                .WriteByte(cell.Metadata).WriteUInt32(cell.Value);
    }
}
