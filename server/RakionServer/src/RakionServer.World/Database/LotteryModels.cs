using System.Collections.Generic;

namespace RakionServer.World.Database
{
    public sealed record LotteryNumbers(byte No1, byte No2, byte No3, byte No4, byte No5)
    {
        public byte[] ToArray() => [No1, No2, No3, No4, No5];
    }

    public sealed record LotteryPurchaseCommand(
        int UserId, string AccountId, byte PaymentType, LotteryNumbers Numbers);

    public enum LotteryPurchaseStatus : byte
    {
        Success = 0,
        InsufficientFunds = 1,
        Rejected = 2
    }

    public sealed record LotteryPurchaseResult(
        LotteryPurchaseStatus Status, int Round, int Gold, int Cash);

    public sealed record LotteryTicket(int Round, LotteryNumbers Numbers);

    public enum LotteryPageStatus : byte
    {
        Success = 0,
        Empty = 1,
        Failed = 2
    }

    public sealed record LotteryPageResult(
        LotteryPageStatus Status, IReadOnlyList<LotteryTicket> Tickets);
}
