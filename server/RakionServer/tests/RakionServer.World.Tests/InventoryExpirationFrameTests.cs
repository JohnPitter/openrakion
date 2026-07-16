using System;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class InventoryExpirationFrameTests
    {
        [Theory]
        [InlineData(0, 4, "310000010000000100000000000400000100000000")]
        [InlineData(13, 119, "310000010d00000100000000007700000100000000")]
        [InlineData(18, -1, "310000011200000100000000011200000100000000")]
        public void ActiveExpirationUsesValidMoveDescriptors(
            byte activeSlot, int emptyBoxSlot, string expected)
        {
            byte[] frame = InventoryExpirationFrames.ActiveSlotClear(activeSlot, emptyBoxSlot);

            Assert.Equal(expected, Convert.ToHexString(frame).ToLowerInvariant());
        }
    }
}
