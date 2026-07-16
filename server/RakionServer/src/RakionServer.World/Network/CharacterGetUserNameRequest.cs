using System;
using System.Text;

namespace RakionServer.World.Network
{
    public readonly record struct CharacterGetUserNameRequest(string Value)
    {
        public bool IsWithinOriginalLimit => Value.Length < 0x0D;

        public static bool TryParse(ReadOnlySpan<byte> payload, out CharacterGetUserNameRequest request)
        {
            request = default;
            int terminator = payload.IndexOf((byte)0);
            if (terminator < 0)
                return false;

            request = new CharacterGetUserNameRequest(
                Encoding.ASCII.GetString(payload[..terminator]));
            return true;
        }
    }
}
