using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class EnchantRulesTests
    {
        [Fact]
        public void ValidateSlots_RejectsEveryDuplicate()
        {
            Assert.Equal(7, EnchantRules.ValidateSlots(1, 1, []));
            Assert.Equal(7, EnchantRules.ValidateSlots(1, 2, [1]));
            Assert.Equal(7, EnchantRules.ValidateSlots(1, 2, [3, 3]));
            Assert.Equal(0, EnchantRules.ValidateSlots(1, 2, [3, 4, 5]));
        }

        [Theory]
        [InlineData(0.80, 0.30, 0.79, 0.99, 0)]
        [InlineData(0.80, 0.30, 0.90, 0.05, 2)]
        [InlineData(0.80, 0.30, 0.90, 0.07, 1)]
        public void Roll_UsesSeparateSuccessAndDowngradeDraws(
            double chance, double downgrade, double first, double second, byte expected) =>
            Assert.Equal(expected, EnchantRules.Roll(chance, downgrade, first, second));

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 0)]
        [InlineData(2, -1)]
        [InlineData(3, -2)]
        [InlineData(4, -3)]
        [InlineData(5, 0)]
        public void Delta_MapsOriginalResultCodes(byte result, int expected) =>
            Assert.Equal(expected, EnchantRules.Delta(result));

        [Fact]
        public void RollOriginal_UsesSixBucketsAndNeutralizesDestroy()
        {
            float[] probabilities = [0.70f, 0.08f, 0.06f, 0.04f, 0.02f, 0.02f];
            Assert.Equal(0, EnchantRules.RollOriginal(probabilities, 0.69f, 4));
            Assert.Equal(1, EnchantRules.RollOriginal(probabilities, 0.71f, 4));
            Assert.Equal(2, EnchantRules.RollOriginal(probabilities, 0.79f, 4));
            Assert.Equal(6, EnchantRules.RollOriginal(probabilities, 0.95f, 4));
            Assert.Equal(4, EnchantRules.RollOriginal([0, 0, 0, 0, 0, 1], 0.5f, 3));
            Assert.Equal(1, EnchantRules.RollOriginal([0, 0, 0, 0, 0, 1], 0.5f, 4));
            Assert.Equal(6, EnchantRules.RollOriginal(probabilities, 1.0f, 4));
        }

        [Fact]
        public void OriginalRollValue_MatchesRand32767Grid()
        {
            Assert.Equal(0.0f, EnchantRules.OriginalRollValue(0));
            Assert.Equal(1.0f, EnchantRules.OriginalRollValue(32767));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => EnchantRules.OriginalRollValue(32768));
        }
    }
}
