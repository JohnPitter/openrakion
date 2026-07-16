using System;

namespace RakionServer.World.Network
{
    public readonly record struct InventoryStackPotionRequest(byte Source, byte Destination)
    {
        public static bool TryParse(ReadOnlySpan<byte> payload, out InventoryStackPotionRequest request)
        {
            request = default;
            if (payload.Length < 2) return false;
            for (int index = 2; index < payload.Length; index++)
                if (payload[index] != 0) return false;
            request = new InventoryStackPotionRequest(payload[0], payload[1]);
            return true;
        }
    }
}
