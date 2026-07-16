using System;
using System.Buffers.Binary;
using RakionServer.World.Database;

namespace RakionServer.World.Network
{
    public static class LotteryFrames
    {
        public static byte[] PurchasePrecheck(
            LotteryPurchaseStatus status, uint gold, uint cash)
        {
            var frame = new byte[11];
            BinaryPrimitives.WriteUInt16LittleEndian(frame, 0x75);
            frame[2] = (byte)status;
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), gold);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(7), cash);
            return frame;
        }

        public static byte[] PurchaseResult(LotteryPurchaseResult result)
        {
            var frame = new byte[15];
            BinaryPrimitives.WriteUInt16LittleEndian(frame, 0x75);
            frame[2] = (byte)result.Status;
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), (uint)result.Round);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(7), (uint)result.Gold);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(11), (uint)result.Cash);
            return frame;
        }

        public static byte[] TicketPage(LotteryPageResult result)
        {
            if (result.Status != LotteryPageStatus.Success)
                return [0x76, 0, (byte)result.Status];
            var frame = new byte[7 + result.Tickets.Count * 9];
            BinaryPrimitives.WriteUInt16LittleEndian(frame, 0x76);
            frame[2] = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3),
                (uint)result.Tickets.Count);
            int offset = 7;
            foreach (LotteryTicket ticket in result.Tickets)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(offset),
                    (uint)ticket.Round);
                ticket.Numbers.ToArray().CopyTo(frame, offset + 4);
                offset += 9;
            }
            return frame;
        }
    }
}
