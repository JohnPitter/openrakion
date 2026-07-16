using System;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class LotteryRulesTests
    {
        [Theory]
        [InlineData((byte)0, 1000)]
        [InlineData((byte)1, 100)]
        public void Cost_UsesOriginalFixedPrice(byte paymentType, int expected) =>
            Assert.Equal(expected, LotteryRules.Cost(paymentType));

        [Fact]
        public void HasRepeatedNumber_ChecksFirstNumberToo()
        {
            Assert.True(LotteryRules.HasRepeatedNumber([7, 2, 3, 4, 7]));
            Assert.False(LotteryRules.HasRepeatedNumber([1, 2, 3, 4, 5]));
        }

        [Fact]
        public void Cost_RejectsUnknownPaymentType() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => LotteryRules.Cost(2));
    }
}
