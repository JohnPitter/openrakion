using System;

namespace RakionServer.World.Network
{
    public readonly record struct InventoryMoveRequest(
        byte SourceType, byte SourceSlot, byte DestinationType, byte DestinationSlot)
    {
        public static bool TryParse(ReadOnlySpan<byte> payload, out InventoryMoveRequest request)
        {
            request = default;
            if (payload.Length < 4)
                return false;

            request = new InventoryMoveRequest(payload[0], payload[1], payload[2], payload[3]);
            return true;
        }
    }
}
