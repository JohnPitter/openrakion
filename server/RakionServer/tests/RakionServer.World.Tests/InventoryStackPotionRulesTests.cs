using RakionServer.World.Database;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class InventoryStackPotionRulesTests
    {
        [Fact]
        public void AcceptsPotionsFromSameCatalogCategory() =>
            Assert.Equal((byte)0, InventoryStackPotionRules.Validate(
                Item(12000, 12), Item(12001, 12)));

        [Fact]
        public void RejectsDifferentCategoriesBeforePotionRange() =>
            Assert.Equal((byte)3, InventoryStackPotionRules.Validate(
                Item(12000, 12), Item(12001, 11)));

        [Fact]
        public void RejectsSameCategoryOutsidePotionRange() =>
            Assert.Equal((byte)4, InventoryStackPotionRules.Validate(
                Item(1000, 1), Item(1001, 1)));

        private static ItemDef Item(int id, byte type) => new() { Id = id, Type = type };
    }
}
