using System;

namespace RakionServer.World.Network
{
    public static class CharacterTutorialClearRequest
    {
        public static bool TryParse(ReadOnlySpan<byte> payload)
        {
            foreach (byte value in payload)
                if (value != 0) return false;
            return true;
        }
    }
}
