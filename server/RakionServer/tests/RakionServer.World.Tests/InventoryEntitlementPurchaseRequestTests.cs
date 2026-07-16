using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class InventoryEntitlementPurchaseRequestTests
    {
        [Fact]
        public void CashPurchase_ParsesWithoutCouponSlot()
        {
            Assert.True(InventoryEntitlementPurchaseRequest.TryParse(new byte[] { 0 }, out var request));
            Assert.False(request.UsesCoupon);
            Assert.Equal((ushort)0, request.CouponSlot);
        }

        [Fact]
        public void CouponPurchase_ParsesSlot()
        {
            Assert.True(InventoryEntitlementPurchaseRequest.TryParse(
                new byte[] { 1, 17, 0 }, out var request));
            Assert.True(request.UsesCoupon);
            Assert.Equal((ushort)17, request.CouponSlot);
        }

        [Fact]
        public void NonBinaryFlag_ReadsSlotButDoesNotUseCoupon()
        {
            Assert.True(InventoryEntitlementPurchaseRequest.TryParse(
                new byte[] { 2, 17, 0 }, out var request));
            Assert.False(request.UsesCoupon);
            Assert.Equal((ushort)17, request.CouponSlot);
        }

        [Fact]
        public void RejectsTruncatedPayloads()
        {
            Assert.False(InventoryEntitlementPurchaseRequest.TryParse(System.Array.Empty<byte>(), out _));
            Assert.False(InventoryEntitlementPurchaseRequest.TryParse(new byte[] { 1 }, out _));
            Assert.False(InventoryEntitlementPurchaseRequest.TryParse(new byte[] { 2, 0 }, out _));
        }
    }
}
