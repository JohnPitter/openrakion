using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class InventoryTimeRulesTests
    {
        [Theory]
        [InlineData(0, 100, false)]
        [InlineData(99, 100, true)]
        [InlineData(100, 100, false)]
        [InlineData(101, 100, false)]
        public void IsExpired_UsesOriginalStrictBoundary(
            int limitTime, long nowMarker, bool expected)
        {
            Assert.Equal(expected, InventoryTimeRules.IsExpired(limitTime, nowMarker));
        }
    }
}
