using System;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class CharacterStateClearRequestTests
    {
        [Fact]
        public void ParsesCashPaymentAndIgnoresCipherPadding()
        {
            Assert.True(CharacterStateClearRequest.TryParse(
                new byte[] { 0, 0xAA, 0xBB }, out var request));
            Assert.Equal((byte)0, request.PaymentType);
            Assert.Equal((ushort)0, request.PaymentValue);
        }

        [Fact]
        public void ParsesCouponCell()
        {
            Assert.True(CharacterStateClearRequest.TryParse(
                new byte[] { 1, 0x34, 0x12 }, out var request));
            Assert.Equal((byte)1, request.PaymentType);
            Assert.Equal((ushort)0x1234, request.PaymentValue);
        }

        [Fact]
        public void RejectsMissingTypeOrTruncatedNonCashPayment()
        {
            Assert.False(CharacterStateClearRequest.TryParse(Array.Empty<byte>(), out _));
            Assert.False(CharacterStateClearRequest.TryParse(new byte[] { 1, 2 }, out _));
        }
    }
}
