namespace RakionServer.World.Domain
{
    public static class LegacyIdentity
    {
        private const int MaxBuddyNameLength = 11;
        private const int MaxCharacterNameLength = 12;

        public static bool IsValidBuddyName(string value) =>
            IsPrintableName(value, MaxBuddyNameLength);

        public static bool IsValidCharacterName(string value) =>
            IsPrintableName(value, MaxCharacterNameLength);

        private static bool IsPrintableName(string value, int maxLength)
        {
            if (value.Length < 1 || value.Length > maxLength) return false;
            foreach (char c in value)
                if (c < 0x21 || c > 0x7e) return false;
            return true;
        }
    }
}
