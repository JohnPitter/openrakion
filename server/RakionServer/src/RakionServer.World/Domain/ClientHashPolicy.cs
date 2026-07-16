using System;

namespace RakionServer.World.Domain
{
    public sealed record ClientHashSettings(bool Enforced, string Md5_1, string Md5_2);

    public static class ClientHashPolicy
    {
        public const byte FieldRequiredReason = 0xBB;
        public const byte HashMismatchReason = 0xBC;

        public static bool LoginAccepted(
            byte mode, string received, ClientHashSettings settings)
        {
            if (!settings.Enforced || mode == 0x04) return true;
            return Matches(mode, received, settings);
        }

        public static byte? FieldDisconnectReason(
            byte mode, bool inField, string received, ClientHashSettings settings)
        {
            if (!settings.Enforced || mode is 0x04 or 0x05) return null;
            if (!inField) return FieldRequiredReason;
            return Matches(mode, received, settings) ? null : HashMismatchReason;
        }

        public static bool IsMd5(string value)
        {
            if (value.Length != 32) return false;
            foreach (char character in value)
                if (!Uri.IsHexDigit(character)) return false;
            return true;
        }

        private static bool Matches(
            byte mode, string received, ClientHashSettings settings)
        {
            string expected = mode == 0x01 ? settings.Md5_2 : settings.Md5_1;
            return string.Equals(received, expected, StringComparison.Ordinal);
        }
    }
}
