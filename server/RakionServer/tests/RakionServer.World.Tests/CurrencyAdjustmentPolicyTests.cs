using System;
using RakionServer.Admin;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class CurrencyAdjustmentPolicyTests
    {
        [Fact]
        public void Validate_AcceptsAuditedCashAdjustment() =>
            CurrencyAdjustmentPolicy.Validate(new CurrencyAdjustmentRequest(
                "account", 1, CurrencyKind.Cash, 5000, "ticket 1234", "admin"));

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        public void Validate_RejectsMissingOrShortReason(string reason) =>
            Assert.Throws<ArgumentException>(() => CurrencyAdjustmentPolicy.Validate(
                new CurrencyAdjustmentRequest(
                    "account", 1, CurrencyKind.Cash, 5000, reason, "admin")));

        [Fact]
        public void Validate_RejectsNegativeBalance() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => CurrencyAdjustmentPolicy.Validate(
                new CurrencyAdjustmentRequest(
                    "account", 1, CurrencyKind.Gold, -1, "correção", "admin")));

        [Fact]
        public void Validate_RequiresGameProfileForGold() =>
            Assert.Throws<ArgumentException>(() => CurrencyAdjustmentPolicy.Validate(
                new CurrencyAdjustmentRequest(
                    "account", 0, CurrencyKind.Gold, 1, "correção", "admin")));
    }
}
