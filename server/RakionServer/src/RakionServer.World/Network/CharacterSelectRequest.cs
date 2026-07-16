using System.Buffers.Binary;
using System;

namespace RakionServer.World.Network
{
    public readonly record struct CharacterSelectRequest(int CharacterId)
    {
        public static bool TryParse(ReadOnlySpan<byte> payload, out CharacterSelectRequest request)
        {
            request = default;
            if (payload.Length < sizeof(int)) return false;
            request = new CharacterSelectRequest(BinaryPrimitives.ReadInt32LittleEndian(payload));
            return true;
        }
    }
}
