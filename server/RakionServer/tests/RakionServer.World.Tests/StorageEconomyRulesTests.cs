using RakionServer.World.Database;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class StorageEconomyRulesTests
    {
        [Theory]
        [InlineData(1, 2700, 0, true, 2700)]
        [InlineData(2, 0, 4800, false, 4800)]
        [InlineData(1, 2700, 0, false, null)]
        [InlineData(2, 0, 4800, true, null)]
        [InlineData(0, 2700, 4800, true, null)]
        [InlineData(1, 0, 0, true, null)]
        public void PurchasePrice_RequiresCatalogCurrencyAndPositivePrice(
            byte shop, int gold, int cash, bool payGold, int? expected)
        {
            var item = new ItemDef { Shop = shop, Gold = gold, Cash = cash };
            Assert.Equal(expected, StorageEconomyRules.PurchasePrice(item, payGold));
        }

        [Theory]
        [InlineData(1, 100, 0, 40)]
        [InlineData(1, 101, 0, 40)]
        [InlineData(2, 0, 101, 152)]
        [InlineData(0, 500, 500, 0)]
        public void SellPrice_MatchesLegacyShopMultipliers(
            byte shop, int gold, int cash, int expected)
        {
            var item = new ItemDef { Shop = shop, Gold = gold, Cash = cash };
            Assert.Equal(expected, StorageEconomyRules.SellPrice(item, 1000));
        }

        [Theory]
        [InlineData(11999, false)]
        [InlineData(12000, true)]
        [InlineData(12999, true)]
        [InlineData(13000, false)]
        public void PotionRange_MatchesOriginalBoundaries(int itemId, bool expected) =>
            Assert.Equal(expected, StorageEconomyRules.IsPotion(itemId));

        [Fact]
        public void PotionsAndMissingDefinitions_HaveNoSaleValue()
        {
            var item = new ItemDef { Shop = 1, Gold = 1000 };
            Assert.Equal(0, StorageEconomyRules.SellPrice(item, 12000));
            Assert.Equal(0, StorageEconomyRules.SellPrice(null, 1000));
        }

        [Theory]
        [InlineData(true, 1)]
        [InlineData(false, 2)]
        public void PurchaseSerialType_MatchesOriginalLedgerNamespace(
            bool payGold, byte expected) =>
            Assert.Equal(expected, StorageEconomyRules.PurchaseSerialType(payGold));
    }
}
