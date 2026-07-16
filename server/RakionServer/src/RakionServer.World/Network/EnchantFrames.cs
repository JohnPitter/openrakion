using System;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public static class EnchantFrames
    {
        public static byte[] Preview(
            PendingEnchant pending, uint fieldHandle, uint secondaryHandle)
        {
            EnchantSelection selection = pending.Selection;
            using var writer = new PacketWriter();
            writer.WriteWord(0).WriteWord(0x28).WriteUInt32(fieldHandle);
            WriteDescriptor(writer, selection.Target.Slot, pending.Serials[0]);
            WriteDescriptor(writer, selection.Catalyst.Slot, pending.Serials[1]);
            writer.WriteByte((byte)selection.Materials.Count);
            for (int i = 0; i < 3; i++)
            {
                if (i < selection.Materials.Count)
                    WriteDescriptor(writer, selection.Materials[i].Slot, pending.Serials[i + 2]);
                else
                    WriteDescriptor(writer, 0, 0);
            }
            writer.WriteByte(0).WriteUInt32(secondaryHandle).WriteByte(0);
            return writer.ToArray();
        }

        public static byte[] Status(byte status)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x74).WriteByte(status);
            return writer.ToArray();
        }

        public static byte[] Result(
            byte result, byte target, byte catalyst, ReadOnlySpan<byte> materials)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x74).WriteByte(result).WriteByte(target)
                .WriteByte(catalyst).WriteByte((byte)materials.Length);
            foreach (byte material in materials) writer.WriteByte(material);
            return writer.ToArray();
        }

        private static void WriteDescriptor(PacketWriter writer, byte slot, uint serial) =>
            writer.WriteByte(slot).WriteUInt32(serial);
    }
}
