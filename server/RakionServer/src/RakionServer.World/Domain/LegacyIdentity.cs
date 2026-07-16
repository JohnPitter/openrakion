namespace RakionServer.World.Domain
{
    public static class LegacyIdentity
    {
        public static bool IsValidBuddyName(string value) => IsPrintableName(value);

        public static bool IsValidCharacterName(string value) => IsPrintableName(value);

        private static bool IsPrintableName(string value)
        {
            if (value.Length is < 1 or > 11) return false;
            foreach (char c in value)
                if (c < 0x21 || c > 0x7e) return false;
            return true;
        }
    }
}
