using RakionServer.World.Database;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class EquipmentRulesTests
    {
        [Theory]
        [InlineData(0, 0, true)]
        [InlineData(0, 1, false)]
        [InlineData(6, 6, true)]
        [InlineData(7, 7, true)]
        [InlineData(7, 9, true)]
        [InlineData(7, 10, false)]
        [InlineData(8, 10, true)]
        [InlineData(10, 12, true)]
        [InlineData(12, 13, true)]
        [InlineData(12, 18, true)]
        [InlineData(12, 12, false)]
        [InlineData(13, 10, true)]
        public void TypeDeterminesActiveSlot(byte type, byte slot, bool expected)
        {
            ItemDef item = Definition(type, classMask: 4, requiredLevel: 1);
            Assert.Equal(expected, EquipmentRules.CanPlace(item, slot, 2, 40));
        }

        [Fact]
        public void CouponTypeNeverEntersActiveSlots()
        {
            ItemDef item = Definition(type: 11, classMask: 31, requiredLevel: 1);
            Assert.False(EquipmentRules.CanPlace(item, 10, 2, 40));
        }

        [Theory]
        [InlineData(5)]
        [InlineData(7)]
        [InlineData(255)]
        public void ClassesOutsideThisClientBuildAreRejected(byte characterClass)
        {
            ItemDef item = Definition(type: 0, classMask: 255, requiredLevel: 1);

            Assert.False(EquipmentRules.CanPlace(item, 0, characterClass, 40));
        }

        [Theory]
        [InlineData(2, 4, 40, true)]
        [InlineData(2, 1, 40, false)]
        [InlineData(2, 4, 19, false)]
        public void ClassMaskAndLevelAreRequired(
            byte characterClass, byte classMask, byte characterLevel, bool expected)
        {
            ItemDef item = Definition(type: 0, classMask, requiredLevel: 20);
            Assert.Equal(expected, EquipmentRules.CanPlace(item, 0, characterClass, characterLevel));
        }

        private static ItemDef Definition(byte type, byte classMask, byte requiredLevel) => new()
        {
            Type = type,
            Class = classMask,
            Level = requiredLevel
        };
    }
}
