using System;
using System.Buffers.Binary;

namespace RakionServer.World.Network
{
    public readonly record struct PresentAcceptRequest(int PendingId, ushort Slot)
    {
        public static bool TryParse(ReadOnlySpan<byte> payload, out PresentAcceptRequest request)
        {
            request = default;
            if (payload.Length < 6)
                return false;
            request = new PresentAcceptRequest(
                BinaryPrimitives.ReadInt32LittleEndian(payload),
                BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]));
            return true;
        }
    }

    public readonly record struct PresentDisposeRequest(int PendingId)
    {
        public static bool TryParse(ReadOnlySpan<byte> payload, out PresentDisposeRequest request)
        {
            request = default;
            if (payload.Length < 4)
                return false;
            request = new PresentDisposeRequest(BinaryPrimitives.ReadInt32LittleEndian(payload));
            return true;
        }
    }
}
