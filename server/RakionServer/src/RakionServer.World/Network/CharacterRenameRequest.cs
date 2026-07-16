using System;
using System.Buffers.Binary;
using System.Text;

namespace RakionServer.World.Network
{
    public readonly record struct CharacterRenameRequest(
        string Name, byte PaymentType, ushort PaymentValue)
    {
        public static bool TryParse(ReadOnlySpan<byte> payload, out CharacterRenameRequest request)
        {
            request = default;
            int terminator = payload.IndexOf((byte)0);
            if (terminator is < 0 or > 11 || payload.Length < terminator + 2) return false;

            byte paymentType = payload[terminator + 1];
            ushort paymentValue = 0;
            if (paymentType != 0)
            {
                int valueOffset = terminator + 2;
                if (payload.Length < valueOffset + sizeof(ushort)) return false;
                paymentValue = BinaryPrimitives.ReadUInt16LittleEndian(payload[valueOffset..]);
            }

            request = new CharacterRenameRequest(
                Encoding.ASCII.GetString(payload[..terminator]), paymentType, paymentValue);
            return true;
        }
    }
}
