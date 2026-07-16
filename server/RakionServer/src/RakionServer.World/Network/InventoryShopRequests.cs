using System;
using System.Buffers.Binary;

namespace RakionServer.World.Network
{
    public readonly record struct InventoryBuyRequest(
        ushort ItemId, byte Currency, byte CouponFlag, ushort CouponSlot)
    {
        public bool UsesCoupon => CouponFlag == 1;
        public bool PaysGold => Currency != 0;

        public static bool TryParse(ReadOnlySpan<byte> payload, out InventoryBuyRequest request)
        {
            request = default;
            if (payload.Length < 4)
                return false;

            byte couponFlag = payload[3];
            if (couponFlag == 1 && payload.Length < 6)
                return false;

            request = new InventoryBuyRequest(
                BinaryPrimitives.ReadUInt16LittleEndian(payload), payload[2], couponFlag,
                couponFlag == 1 ? BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]) : (ushort)0);
            return true;
        }
    }

    public readonly record struct InventorySellRequest(byte Slot)
    {
        public static bool TryParse(ReadOnlySpan<byte> payload, out InventorySellRequest request)
        {
            request = default;
            if (payload.Length < 1)
                return false;
            request = new InventorySellRequest(payload[0]);
            return true;
        }
    }
}
