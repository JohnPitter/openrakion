using System;
using System.Buffers.Binary;

namespace RakionServer.World.Network
{
    public readonly record struct PowerUserPurchaseRequest(
        byte Mode, byte CouponFlag, ushort CouponSlot)
    {
        public bool UsesCoupon => CouponFlag == 1;

        public static bool TryParse(ReadOnlySpan<byte> payload, out PowerUserPurchaseRequest request)
        {
            request = default;
            if (payload.Length < 2 || payload[0] > 1)
                return false;

            byte couponFlag = payload[1];
            if (couponFlag != 0 && payload.Length < 4)
                return false;

            request = new PowerUserPurchaseRequest(
                payload[0], couponFlag,
                couponFlag == 0 ? (ushort)0 : BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]));
            return true;
        }
    }
}
