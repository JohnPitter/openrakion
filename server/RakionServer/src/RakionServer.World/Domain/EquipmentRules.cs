using RakionServer.World.Database;

namespace RakionServer.World.Domain
{
    public static class EquipmentRules
    {
        public const byte ActiveSlotCount = 19;
        public const byte GearSlotCount = 7;
        public const byte CharacterClassCount = 5;

        public static bool CanPlace(ItemDef item, byte slot, byte characterClass, byte characterLevel)
        {
            if (slot >= ActiveSlotCount || characterClass >= CharacterClassCount ||
                characterLevel < item.Level ||
                item.Type == 11)
                return false;
            if ((item.Class & (1 << characterClass)) == 0)
                return false;

            return item.Type switch
            {
                < 7 => slot == item.Type,
                7 => slot is >= 7 and <= 9,
                12 => slot is >= 13 and <= 18,
                _ => slot is >= 10 and <= 12
            };
        }

        public static bool IsGearSlot(byte slot) => slot < GearSlotCount;
    }
}
