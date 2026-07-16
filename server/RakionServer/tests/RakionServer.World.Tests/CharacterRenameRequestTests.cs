using System;
using System.Text;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class CharacterRenameRequestTests
    {
        [Fact]
        public void ParsesCashPaymentAndIgnoresCipherPadding()
        {
            Assert.True(CharacterRenameRequest.TryParse(
                Bytes("ProbeRename\0", 0, 0xAA), out var request));
            Assert.Equal("ProbeRename", request.Name);
            Assert.Equal((byte)0, request.PaymentType);
            Assert.Equal((ushort)0, request.PaymentValue);
        }

        [Fact]
        public void ParsesCouponCell()
        {
            Assert.True(CharacterRenameRequest.TryParse(
                Bytes("ProbeRename\0", 1, 0x34, 0x12), out var request));
            Assert.Equal((byte)1, request.PaymentType);
            Assert.Equal((ushort)0x1234, request.PaymentValue);
        }

        [Fact]
        public void RejectsMissingTerminatorAndTruncatedNonCashPayment()
        {
            Assert.False(CharacterRenameRequest.TryParse(Encoding.ASCII.GetBytes("ProbeRename"), out _));
            Assert.False(CharacterRenameRequest.TryParse(Bytes("ProbeRename\0", 1, 2), out _));
        }

        [Fact]
        public void RejectsNameAboveWireBoundary()
        {
            Assert.False(CharacterRenameRequest.TryParse(Bytes("123456789012\0", 0), out _));
        }

        private static byte[] Bytes(string text, params byte[] suffix)
        {
            byte[] prefix = Encoding.ASCII.GetBytes(text);
            byte[] result = new byte[prefix.Length + suffix.Length];
            prefix.CopyTo(result, 0);
            suffix.CopyTo(result, prefix.Length);
            return result;
        }
    }
}
