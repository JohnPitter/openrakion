using System;
using System.Buffers.Binary;
using System.Text;
using RakionServer.Buddy;
using RakionServer.Common;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class BuddySmsProtocolTests
    {
        [Fact]
        public void Credential_UsesZeroKeyAesAndCarriesAccountAndSeed()
        {
            byte[] encrypted = BuddyCrypto.CreateCredential("test", 0x11223344);

            Assert.Equal(
                "F016E588FEA1B726F92999D1ED904D56F574CD1436CDC3A12E0D9747200AE64B",
                Convert.ToHexString(encrypted));
            byte[] login = new byte[BuddyCrypto.LoginPayloadLength];
            encrypted.CopyTo(login, 0);
            Assert.True(BuddyCrypto.TryReadCredential(login, out BuddyCredential credential));
            Assert.Equal("test", credential.AccountId);
            Assert.Equal(0x11223344u, credential.Seed);
        }

        [Fact]
        public void LoginEnvelope_OpensWithDerivedSha1SessionKey()
        {
            const uint seed = 0x11223344;
            byte[] clear = new byte[0x84];
            BinaryPrimitives.WriteUInt32LittleEndian(clear, 0x1B);
            var crypto = new PacketCrypto();
            crypto.Enable(BuddyCrypto.DeriveSessionKey("test", "test", seed),
                BuddyCrypto.SessionMarker);
            byte[] payload = new byte[BuddyCrypto.LoginPayloadLength];
            BuddyCrypto.CreateCredential("test", seed).CopyTo(payload, 0);
            crypto.Encrypt(clear).CopyTo(payload, BuddyCrypto.CredentialLength);

            Assert.True(BuddyCrypto.TryOpenLogin(payload, "test", seed,
                out BuddyCredential credential, out PacketCrypto opened, out byte[] result));
            Assert.Equal("test", credential.AccountId);
            Assert.True(opened.Enabled);
            Assert.Equal(clear, result);
        }

        [Fact]
        public void SmsSend_RoundTripsOriginalFixedLayout()
        {
            byte[] clear = BuddySmsCodec.BuildSend("target", "hello");

            Assert.Equal(36, clear.Length);
            Assert.Equal("7461726765740000000000000000000000000000050068656C6C6F000000000000000000",
                Convert.ToHexString(clear));
            Assert.True(BuddySmsCodec.TryParseSend(clear, out string target, out string text));
            Assert.Equal("target", target);
            Assert.Equal("hello", text);
        }

        [Fact]
        public void SavedSms_UsesRecordConsumedByNtfSavePacket()
        {
            DateTime created = DateTime.UnixEpoch.AddSeconds(1234);
            byte[] clear = BuddySmsCodec.BuildSavedBatch([
                new BuddySmsMessage(7, "alice", "Alice", "bob", "hi", created)]);

            Assert.Equal(76, clear.Length);
            Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(clear));
            Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(clear.AsSpan(2)));
            Assert.Equal("alice", ReadCString(clear.AsSpan(6, 20)));
            Assert.Equal("Alice", ReadUtf16(clear.AsSpan(26, 40)));
            Assert.Equal(1234u, BinaryPrimitives.ReadUInt32LittleEndian(clear.AsSpan(66)));
            Assert.Equal(BuddySmsCodec.P2PSendSms,
                BinaryPrimitives.ReadUInt16LittleEndian(clear.AsSpan(70)));
            Assert.Equal((ushort)2, BinaryPrimitives.ReadUInt16LittleEndian(clear.AsSpan(72)));
            Assert.Equal("hi", Encoding.Latin1.GetString(clear.AsSpan(74, 2)));
        }

        [Fact]
        public void SavedPacketAck_RejectsTruncatedIdList()
        {
            Assert.False(BuddySmsCodec.TryParseAcknowledgement(
                [2, 0, 1, 0, 0, 0], out _));
            Assert.True(BuddySmsCodec.TryParseAcknowledgement(
                [1, 0, 7, 0, 0, 0], out uint[] ids));
            Assert.Equal([7u], ids);
        }

        private static string ReadCString(ReadOnlySpan<byte> bytes)
        {
            int length = bytes.IndexOf((byte)0);
            return Encoding.Latin1.GetString(bytes[..length]);
        }

        private static string ReadUtf16(ReadOnlySpan<byte> bytes)
        {
            int length = 0;
            while (length + 1 < bytes.Length && (bytes[length] != 0 || bytes[length + 1] != 0))
                length += 2;
            return Encoding.Unicode.GetString(bytes[..length]);
        }
    }
}
