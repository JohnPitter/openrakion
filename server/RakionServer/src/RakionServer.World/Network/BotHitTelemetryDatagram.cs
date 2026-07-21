using System;
using System.Buffers.Binary;

namespace RakionServer.World.Network
{
    public readonly record struct BotHitTelemetry(uint Sequence, byte TargetSeat);

    public static class BotHitTelemetryDatagram
    {
        public const ushort Type = 0xb07b;
        public const int Size = 7;

        public static byte[] Build(uint sequence, byte targetSeat)
        {
            if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            byte[] packet = new byte[Size];
            BinaryPrimitives.WriteUInt16LittleEndian(packet, Type);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(2), sequence);
            packet[6] = targetSeat;
            return packet;
        }

        public static bool TryParse(ReadOnlySpan<byte> packet, out BotHitTelemetry telemetry)
        {
            telemetry = default;
            if (packet.Length != Size ||
                BinaryPrimitives.ReadUInt16LittleEndian(packet) != Type)
                return false;

            uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(packet[2..]);
            if (sequence == 0) return false;
            telemetry = new BotHitTelemetry(sequence, packet[6]);
            return true;
        }
    }
}
