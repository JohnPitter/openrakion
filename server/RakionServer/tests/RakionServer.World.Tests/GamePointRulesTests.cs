using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class GamePointRulesTests
    {
        [Theory]
        [InlineData(0, 1, 1, 1500, 500)]
        [InlineData(1, 1, 3, 100, 100)]
        [InlineData(1, 3, 3, 80, 200)]
        [InlineData(2, 1, 1, 115, 160)]
        [InlineData(3, 1, 1, 90, 70)]
        [InlineData(4, 1, 1, 115, 160)]
        public void OriginalLimits_AcceptBoundary(
            byte mode, byte round, byte maxRounds, uint exp, uint gold)
        {
            Assert.True(GamePointRules.IsValid(mode, round, maxRounds, exp, gold));
            Assert.False(GamePointRules.IsValid(mode, round, maxRounds, exp + 1, gold));
            Assert.False(GamePointRules.IsValid(mode, round, maxRounds, exp, gold + 1));
        }
    }
}
