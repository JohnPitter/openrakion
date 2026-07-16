using System;
using System.Buffers.Binary;

namespace RakionServer.World.Network
{
    public readonly record struct InventoryEntitlementPurchaseRequest(
        byte CouponFlag, ushort CouponSlot)
    {
        public bool UsesCoupon => CouponFlag == 1;

        public static bool TryParse(
            ReadOnlySpan<byte> payload, out InventoryEntitlementPurchaseRequest request)
        {
            request = default;
            if (payload.Length < 1)
                return false;

            byte couponFlag = payload[0];
            if (couponFlag != 0 && payload.Length < 3)
                return false;

            request = new InventoryEntitlementPurchaseRequest(
                couponFlag,
                couponFlag == 0 ? (ushort)0 : BinaryPrimitives.ReadUInt16LittleEndian(payload[1..]));
            return true;
        }
    }
}
