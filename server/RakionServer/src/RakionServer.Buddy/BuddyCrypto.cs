using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using RakionServer.Common;

namespace RakionServer.Buddy
{
    public readonly record struct BuddyCredential(string AccountId, uint Seed);

    public static class BuddyCrypto
    {
        public const int LoginPayloadLength = 0xD0;
        public const int CredentialLength = 32;
        public const uint SessionMarker = 0x2DBABE65;
        private static readonly byte[] CredentialKey =
            Convert.FromHexString("2C45926CF3396642B670D006A1FA8182");

        public static bool TryReadCredential(
            ReadOnlySpan<byte> payload, out BuddyCredential credential)
        {
            credential = default;
            if (payload.Length != LoginPayloadLength) return false;
            byte[] clear = TransformEcb(payload[..CredentialLength], CredentialKey, false);
            int terminator = clear.AsSpan(0, 20).IndexOf((byte)0);
            if (terminator <= 0) return false;
            string account = Encoding.Latin1.GetString(clear, 0, terminator);
            if (!IsLegacyIdentity(account)) return false;
            uint seed = BinaryPrimitives.ReadUInt32LittleEndian(clear.AsSpan(20, 4));
            credential = new BuddyCredential(account, seed);
            return true;
        }

        public static byte[] DeriveSessionKey(string accountId, uint seed)
        {
            byte[] account = Encoding.Latin1.GetBytes(accountId);
            int accountLength = Math.Min(20, account.Length);
            byte[] material = new byte[accountLength + 4];
            account.AsSpan(0, accountLength).CopyTo(material);
            BinaryPrimitives.WriteUInt32LittleEndian(material.AsSpan(material.Length - 4), seed);
            return BuddySha0.ComputeRawState(material)[..16];
        }

        public static bool TryOpenLogin(
            ReadOnlySpan<byte> payload, uint expectedSeed,
            out BuddyCredential credential, out PacketCrypto crypto, out byte[] clear)
        {
            crypto = new PacketCrypto();
            clear = Array.Empty<byte>();
            if (!TryReadCredential(payload, out credential) || credential.Seed != expectedSeed)
                return false;
            crypto.Enable(DeriveSessionKey(credential.AccountId, credential.Seed), SessionMarker);
            if (!crypto.TryDecrypt(payload[CredentialLength..], out clear) ||
                clear.Length != 0x84 || BinaryPrimitives.ReadUInt32LittleEndian(clear) != 0x1B)
            {
                crypto.Disable();
                return false;
            }
            return true;
        }

        public static byte[] CreateCredential(string accountId, uint seed)
        {
            byte[] clear = new byte[CredentialLength];
            byte[] account = Encoding.Latin1.GetBytes(accountId);
            account.AsSpan(0, Math.Min(20, account.Length)).CopyTo(clear);
            BinaryPrimitives.WriteUInt32LittleEndian(clear.AsSpan(20, 4), seed);
            return TransformEcb(clear, CredentialKey, true);
        }

        private static byte[] TransformEcb(ReadOnlySpan<byte> input, byte[] key, bool encrypt)
        {
            byte[] output = new byte[input.Length];
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            using ICryptoTransform transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor();
            transform.TransformBlock(input.ToArray(), 0, input.Length, output, 0);
            return output;
        }

        private static bool IsLegacyIdentity(string value)
        {
            if (value.Length is < 1 or > 16) return false;
            foreach (char character in value)
                if (!(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')) return false;
            return true;
        }
    }
}
