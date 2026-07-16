namespace RakionServer.World.Domain
{
    public readonly record struct FieldPotionUseContext(
        byte Cell,
        int RequestedItemId,
        byte FieldMode,
        byte UnlockedSlots,
        int EquippedItemId,
        int Count,
        bool Used);

    public readonly record struct FieldPotionReservationState(
        int AvailableCount,
        int PendingCount);

    public static class FieldPotionRules
    {
        public static bool CanUse(FieldPotionUseContext context) =>
            InventoryEntitlementRules.IsPotionCellUnlocked(context.Cell, context.UnlockedSlots) &&
            StorageEconomyRules.IsPotion(context.RequestedItemId) &&
            context.EquippedItemId == context.RequestedItemId &&
            context.Count > 0 &&
            (context.FieldMode == 0 || !context.Used);

        public static bool TryReserve(
            FieldPotionReservationState current,
            out FieldPotionReservationState next)
        {
            next = current;
            if (current.AvailableCount <= 0 || current.PendingCount < 0) return false;
            next = new FieldPotionReservationState(
                current.AvailableCount - 1,
                current.PendingCount + 1);
            return true;
        }

        public static FieldPotionReservationState Commit(
            FieldPotionReservationState current, int databaseRemaining)
        {
            int pending = System.Math.Max(0, current.PendingCount - 1);
            int available = System.Math.Max(0, databaseRemaining - pending);
            return new FieldPotionReservationState(available, pending);
        }

        public static FieldPotionReservationState Fail(
            FieldPotionReservationState current) =>
            current with { PendingCount = System.Math.Max(0, current.PendingCount - 1) };
    }
}
