namespace RakionServer.World.Domain
{
    public static class InventoryMoveRules
    {
        public const byte BoxSlotCount = 120;
        public const byte ActiveSlotCount = 19;

        public static bool IsCoordinateValid(byte type, byte slot) =>
            type == 0 ? slot < BoxSlotCount : slot < ActiveSlotCount;
    }
}
