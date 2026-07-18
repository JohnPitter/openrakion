using System;
using System.Buffers.Binary;

namespace RakionServer.World.Network
{
    public static class BotTelemetryDatagram
    {
        public const ushort Type = 0xb07a;
        public const int HeaderSize = 4;

        public static byte[] Wrap(ReadOnlySpan<byte> gameplay)
        {
            if (gameplay.Length == 0 || gameplay.Length > UdpGameplay.MaxPacket - HeaderSize)
                throw new ArgumentOutOfRangeException(nameof(gameplay));

            byte[] packet = new byte[HeaderSize + gameplay.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(packet, Type);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), (ushort)gameplay.Length);
            gameplay.CopyTo(packet.AsSpan(HeaderSize));
            return packet;
        }

        public static bool TryUnwrap(ReadOnlySpan<byte> packet, out ReadOnlySpan<byte> gameplay)
        {
            gameplay = default;
            if (packet.Length <= HeaderSize ||
                BinaryPrimitives.ReadUInt16LittleEndian(packet) != Type)
                return false;

            int length = BinaryPrimitives.ReadUInt16LittleEndian(packet[2..]);
            if (length != packet.Length - HeaderSize) return false;
            gameplay = packet[HeaderSize..];
            return true;
        }
    }
}
