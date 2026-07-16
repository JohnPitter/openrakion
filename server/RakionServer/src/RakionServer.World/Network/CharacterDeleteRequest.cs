using System;
using System.Buffers.Binary;
using System.Text;

namespace RakionServer.World.Network
{
    public readonly record struct CharacterDeleteRequest(int CharacterId, string DeleteKey)
    {
        public static bool TryParse(ReadOnlySpan<byte> payload, out CharacterDeleteRequest request)
        {
            request = default;
            if (payload.Length < 5) return false;

            ReadOnlySpan<byte> encodedKey = payload[sizeof(int)..];
            int terminator = encodedKey.IndexOf((byte)0);
            if (terminator < 0 && encodedKey.Length < 10) return false;
            int keyLength = Math.Min(terminator < 0 ? encodedKey.Length : terminator, 10);
            request = new CharacterDeleteRequest(
                BinaryPrimitives.ReadInt32LittleEndian(payload),
                Encoding.ASCII.GetString(encodedKey[..keyLength]));
            return true;
        }
    }
}
