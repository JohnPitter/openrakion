using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class EnchantFrameGoldenTests
    {
        private static readonly EnchantSelection Selection = new(
            1, new EnchantItemRef(1, 101, 3001), 4,
            new EnchantItemRef(2, 102, 13001),
            [new EnchantItemRef(3, 103, 14001)]);

        [Fact]
        public void Preview_MatchesOriginalTwoPhaseLayout()
        {
            var pending = new PendingEnchant(Selection, 0, 5, 0.5, 7,
                [0x11223344, 0x55667788, 0x99aabbcc], "op");
            Assert.Equal(
                "0000280044332211014433221102887766550103ccbbaa9900000000000000000000008877665500",
                System.Convert.ToHexString(EnchantFrames.Preview(
                    pending, 0x11223344, 0x55667788)).ToLowerInvariant());
        }

        [Fact]
        public void Result_AndError_MatchOriginalLayouts()
        {
            Assert.Equal("74000201020103", System.Convert.ToHexString(
                EnchantFrames.Result(2, 1, 2, [3])).ToLowerInvariant());
            Assert.Equal("740008", System.Convert.ToHexString(
                EnchantFrames.Status(8)).ToLowerInvariant());
        }
    }
}
