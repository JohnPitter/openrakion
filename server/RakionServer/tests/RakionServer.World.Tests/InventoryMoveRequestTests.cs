using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class InventoryMoveRequestTests
    {
        [Fact]
        public void TryParse_ReadsFourCoordinates()
        {
            Assert.True(InventoryMoveRequest.TryParse(
                new byte[] { 0, 119, 1, 18, 0, 0 }, out var request));
            Assert.Equal(new InventoryMoveRequest(0, 119, 1, 18), request);
        }

        [Fact]
        public void TryParse_RejectsTruncatedPayload() =>
            Assert.False(InventoryMoveRequest.TryParse(new byte[3], out _));

        [Theory]
        [InlineData(0, 119, true)]
        [InlineData(0, 120, false)]
        [InlineData(1, 18, true)]
        [InlineData(2, 18, true)]
        [InlineData(1, 19, false)]
        public void CoordinateValidation_MatchesOriginalTypeBranches(
            byte type, byte slot, bool expected) =>
            Assert.Equal(expected, InventoryMoveRules.IsCoordinateValid(type, slot));
    }
}
