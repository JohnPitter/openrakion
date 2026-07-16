using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class PowerUserPurchaseRequestTests
    {
        [Fact]
        public void InitialCashPurchase_ParsesWithoutCoupon()
        {
            Assert.True(PowerUserPurchaseRequest.TryParse(new byte[] { 0, 0 }, out var request));
            Assert.Equal((byte)0, request.Mode);
            Assert.False(request.UsesCoupon);
        }

        [Fact]
        public void RenewalCouponPurchase_ParsesSlot()
        {
            Assert.True(PowerUserPurchaseRequest.TryParse(
                new byte[] { 1, 1, 23, 0 }, out var request));
            Assert.Equal((byte)1, request.Mode);
            Assert.True(request.UsesCoupon);
            Assert.Equal((ushort)23, request.CouponSlot);
        }

        [Fact]
        public void NonBinaryCouponFlag_ReadsSlotButDoesNotUseCoupon()
        {
            Assert.True(PowerUserPurchaseRequest.TryParse(
                new byte[] { 0, 2, 23, 0 }, out var request));
            Assert.False(request.UsesCoupon);
            Assert.Equal((ushort)23, request.CouponSlot);
        }

        [Fact]
        public void RejectsInvalidModeAndTruncatedPayloads()
        {
            Assert.False(PowerUserPurchaseRequest.TryParse(new byte[] { 2, 0 }, out _));
            Assert.False(PowerUserPurchaseRequest.TryParse(new byte[] { 0 }, out _));
            Assert.False(PowerUserPurchaseRequest.TryParse(new byte[] { 0, 1, 0 }, out _));
        }
    }
}
