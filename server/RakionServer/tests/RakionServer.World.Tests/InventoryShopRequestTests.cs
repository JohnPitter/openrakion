using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class InventoryShopRequestTests
    {
        [Fact]
        public void Buy_ParsesOptionalCouponSlot()
        {
            Assert.True(InventoryBuyRequest.TryParse(
                new byte[] { 0xE9, 0x03, 1, 1, 16, 0 }, out var request));
            Assert.Equal(new InventoryBuyRequest(1001, 1, 1, 16), request);
            Assert.True(request.PaysGold);
            Assert.True(request.UsesCoupon);
        }

        [Fact]
        public void Buy_TreatsNonBinaryFlagsLikeOriginal()
        {
            Assert.True(InventoryBuyRequest.TryParse(
                new byte[] { 1, 0, 2, 2 }, out var request));
            Assert.True(request.PaysGold);
            Assert.False(request.UsesCoupon);
        }

        [Fact]
        public void Buy_RejectsTruncatedCouponSlot() =>
            Assert.False(InventoryBuyRequest.TryParse(new byte[] { 1, 0, 0, 1 }, out _));

        [Fact]
        public void Sell_ParsesSlotAndRejectsEmptyPayload()
        {
            Assert.True(InventorySellRequest.TryParse(new byte[] { 119 }, out var request));
            Assert.Equal((byte)119, request.Slot);
            Assert.False(InventorySellRequest.TryParse(System.Array.Empty<byte>(), out _));
        }
    }
}
