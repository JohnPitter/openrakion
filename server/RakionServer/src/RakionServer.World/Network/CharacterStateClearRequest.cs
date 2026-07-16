using System;
using System.Buffers.Binary;

namespace RakionServer.World.Network
{
    public readonly record struct CharacterStateClearRequest(byte PaymentType, ushort PaymentValue)
    {
        public static bool TryParse(ReadOnlySpan<byte> payload, out CharacterStateClearRequest request)
        {
            request = default;
            if (payload.IsEmpty) return false;

            byte paymentType = payload[0];
            if (paymentType == 0)
            {
                request = new CharacterStateClearRequest(0, 0);
                return true;
            }

            if (payload.Length < 3) return false;
            request = new CharacterStateClearRequest(
                paymentType, BinaryPrimitives.ReadUInt16LittleEndian(payload[1..]));
            return true;
        }
    }
}
