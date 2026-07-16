using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class CharacterEconomyRulesTests
    {
        [Theory]
        [InlineData(7000, 50, 3500, 3500)]
        [InlineData(3000, 33, 2000, 1000)]
        [InlineData(12000, 0, 12000, 0)]
        [InlineData(19000, 100, 0, 19000)]
        public void CouponQuote_MatchesOriginalHundredRounding(
            int baseCost, int rate, int expectedCost, int expectedLoggedDiscount)
        {
            var quote = CharacterEconomyRules.ApplyCoupon(baseCost, rate);
            Assert.Equal(expectedCost, quote.FinalCost);
            Assert.Equal(expectedLoggedDiscount, quote.LoggedDiscount);
        }

        [Theory]
        [InlineData(0, 10000)]
        [InlineData(1, 10001)]
        [InlineData(2, 0)]
        public void PowerUserProduct_MatchesInitialAndRenewalLedger(byte mode, int expected) =>
            Assert.Equal(expected, CharacterEconomyRules.PowerUserProduct(mode));

        [Theory]
        [InlineData(3000, 0, 229, 3, 1243)]
        [InlineData(3000, 0, 230, 0, 1040)]
        [InlineData(3000, 0, 999, 2, 1042)]
        [InlineData(3000, 0, 1000, 0, null)]
        [InlineData(5001, 4, 856, 3, 5243)]
        [InlineData(5001, 4, 3999, 1, 5041)]
        [InlineData(5001, 4, 4000, 1, null)]
        public void PresentSelection_MatchesThresholdsAndClassCatalog(
            int cost, byte characterClass, int roll, int variant, int? expected) =>
            Assert.Equal(expected, CharacterEconomyRules.SelectPresent(cost, characterClass, roll, variant));

        [Theory]
        [InlineData(0, 1)]
        [InlineData(999, 0)]
        [InlineData(32767, 741824)]
        public void LegacyPresentRoll_SquaresMsvcRandBeforeModulo(int random, int expected) =>
            Assert.Equal(expected, CharacterEconomyRules.LegacyPresentRoll(random));
    }
}
