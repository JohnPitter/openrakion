using System;
using System.Buffers.Binary;

namespace RakionServer.World.Network
{
    public static class BotTelemetryDatagram
    {
        public const ushort Type = 0xb07a;
        public const ushort HeadlessType = 0xb07b;
        public const int HeaderSize = 4;

        public static byte[] Wrap(ReadOnlySpan<byte> gameplay, bool headlessRelay = false)
        {
            if (gameplay.Length == 0 || gameplay.Length > UdpGameplay.MaxPacket - HeaderSize)
                throw new ArgumentOutOfRangeException(nameof(gameplay));

            byte[] packet = new byte[HeaderSize + gameplay.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(
                packet, headlessRelay ? HeadlessType : Type);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), (ushort)gameplay.Length);
            gameplay.CopyTo(packet.AsSpan(HeaderSize));
            return packet;
        }

        public static bool TryUnwrap(ReadOnlySpan<byte> packet, out ReadOnlySpan<byte> gameplay)
            => TryUnwrap(packet, out gameplay, out _);

        public static bool TryUnwrap(
            ReadOnlySpan<byte> packet, out ReadOnlySpan<byte> gameplay,
            out bool headlessRelay)
        {
            gameplay = default;
            headlessRelay = false;
            ushort type = packet.Length >= sizeof(ushort)
                ? BinaryPrimitives.ReadUInt16LittleEndian(packet)
                : (ushort)0;
            if (packet.Length <= HeaderSize ||
                (type != Type && type != HeadlessType))
                return false;

            int length = BinaryPrimitives.ReadUInt16LittleEndian(packet[2..]);
            if (length != packet.Length - HeaderSize) return false;
            gameplay = packet[HeaderSize..];
            headlessRelay = type == HeadlessType;
            return true;
        }
    }
}
