using System;
using System.Text;

namespace RakionServer.World.Network
{
    public readonly record struct BuddyNameChangeRequest(string Name)
    {
        public static bool TryParse(ReadOnlySpan<byte> payload, out BuddyNameChangeRequest request)
        {
            request = default;
            int terminator = payload.IndexOf((byte)0);
            if (terminator is < 1 or > 11) return false;
            request = new BuddyNameChangeRequest(Encoding.ASCII.GetString(payload[..terminator]));
            return true;
        }
    }
}
