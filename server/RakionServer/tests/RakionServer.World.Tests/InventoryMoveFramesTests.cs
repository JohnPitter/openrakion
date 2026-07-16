using System;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class InventoryMoveFramesTests
    {
        [Fact]
        public void Response_MatchesOriginalTwentyOneByteLayout()
        {
            byte[] frame = InventoryMoveFrames.Response(0,
                new InventoryMoveCell(1, 18, 12000, 0, 5),
                new InventoryMoveCell(0, 119, 100, 2, 0));

            Assert.Equal("3100000112E02E0005000000007764000200000000",
                Convert.ToHexString(frame));
        }
    }
}
