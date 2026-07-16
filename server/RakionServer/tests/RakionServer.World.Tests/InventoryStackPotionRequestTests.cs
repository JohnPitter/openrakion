using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class InventoryStackPotionRequestTests
    {
        [Fact]
        public void ParsesTwoSlotsAndCipherPadding()
        {
            Assert.True(InventoryStackPotionRequest.TryParse(
                new byte[] { 4, 9, 0, 0, 0, 0, 0, 0 }, out var request));
            Assert.Equal((byte)4, request.Source);
            Assert.Equal((byte)9, request.Destination);
        }

        [Theory]
        [InlineData(new byte[] { 1 })]
        [InlineData(new byte[] { 1, 2, 3 })]
        public void RejectsTruncationAndNonZeroPadding(byte[] payload) =>
            Assert.False(InventoryStackPotionRequest.TryParse(payload, out _));
    }
}
