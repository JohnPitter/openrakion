using RakionServer.World.Database;

namespace RakionServer.World.Domain
{
    public static class InventoryStackPotionRules
    {
        public static byte Validate(ItemDef? source, ItemDef? destination)
        {
            if (source == null || destination == null || source.Type != destination.Type)
                return 3;
            if (!StorageEconomyRules.IsPotion(source.Id) ||
                !StorageEconomyRules.IsPotion(destination.Id))
                return 4;
            return 0;
        }
    }
}
