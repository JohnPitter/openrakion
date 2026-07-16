using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class KeepAliveRulesTests
    {
        [Theory]
        [InlineData(90_000, false)]
        [InlineData(90_001, true)]
        public void IsLate_MatchesOriginalStrictThreshold(long elapsed, bool expected) =>
            Assert.Equal(expected, KeepAliveRules.IsLate(elapsed));
    }
}
