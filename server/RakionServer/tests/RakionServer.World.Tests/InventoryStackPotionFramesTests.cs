using System;
using System.Collections.Generic;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class InventoryStackPotionFramesTests
    {
        [Fact]
        public void ErrorIsExactThreeByteLogicalFrame() =>
            Assert.Equal("730004", Hex(InventoryStackPotionFrames.Error(4)));

        [Fact]
        public void SuccessBodyUsesCanonicalIdentityAndInventoryList()
        {
            var box = new List<int>(new int[120]);
            box[2] = 12000;
            box[9] = 12001;

            Assert.Equal(
                "2a0000000209000000000000000000000000630000000002e02e0000e12e0000020900",
                Hex(InventoryStackPotionFrames.SuccessBody(42, 99, 2, 9, box)));
        }

        private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
