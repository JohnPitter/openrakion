using System;
using System.Text;

namespace RakionServer.World.Network
{
    public readonly record struct CharacterCreateRequest(string Name, byte Class, byte Slot)
    {
        public static bool TryParse(ReadOnlySpan<byte> payload, out CharacterCreateRequest request)
        {
            request = default;
            int terminator = payload.IndexOf((byte)0);
            if (terminator is < 0 or >= 13 || payload.Length < terminator + 3) return false;

            request = new CharacterCreateRequest(
                Encoding.ASCII.GetString(payload[..terminator]),
                payload[terminator + 1], payload[terminator + 2]);
            return true;
        }
    }
}
