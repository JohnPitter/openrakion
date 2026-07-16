using System;
using RakionServer.World.Database;

namespace RakionServer.World.Domain
{
    public static class InventoryEntitlementRules
    {
        public const int StageLevelFreeProduct = 10014;
        public const long StageLevelFreeCooldownMinutes = 1440;

        public static int Price(InventoryEntitlement entitlement) => entitlement switch
        {
            InventoryEntitlement.Bag => 8000,
            InventoryEntitlement.CharacterSlot => 12000,
            _ => throw new ArgumentOutOfRangeException(nameof(entitlement))
        };

        public static int ProductId(InventoryEntitlement entitlement) => entitlement switch
        {
            InventoryEntitlement.Bag => 10006,
            InventoryEntitlement.CharacterSlot => 10007,
            _ => throw new ArgumentOutOfRangeException(nameof(entitlement))
        };

        public static byte Maximum(InventoryEntitlement entitlement) => entitlement switch
        {
            InventoryEntitlement.Bag => 3,
            InventoryEntitlement.CharacterSlot => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(entitlement))
        };

        public static int PotionSlotProduct(byte currentSlots) => currentSlots switch
        {
            3 => 10008,
            4 => 10009,
            5 => 10010,
            _ => 0
        };

        public static bool IsPotionCellUnlocked(int cell, byte potionSlots) =>
            cell >= 13 && cell < 13 + Math.Min(potionSlots, (byte)6);

        public static int StageRankClearProduct(byte level) => level switch
        {
            >= 10 and <= 20 => 10011,
            >= 21 and <= 40 => 10012,
            > 40 => 10013,
            _ => 0
        };

        public static bool CanPurchaseStageLevelFree(long currentMarker, long nowMarker) =>
            currentMarker <= nowMarker - StageLevelFreeCooldownMinutes - 1;
    }
}
