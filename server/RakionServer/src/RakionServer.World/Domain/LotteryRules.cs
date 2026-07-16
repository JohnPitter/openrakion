using System;

namespace RakionServer.World.Domain
{
    public static class LotteryRules
    {
        public const int GoldCost = 1000;
        public const int CashCost = 100;
        public const int NumbersPerTicket = 5;
        public const int PageSize = 10;

        public static bool IsPaymentType(byte paymentType) => paymentType is 0 or 1;

        public static int Cost(byte paymentType) => paymentType switch
        {
            0 => GoldCost,
            1 => CashCost,
            _ => throw new ArgumentOutOfRangeException(nameof(paymentType))
        };

        public static bool HasRepeatedNumber(ReadOnlySpan<byte> numbers)
        {
            if (numbers.Length != NumbersPerTicket) return true;
            for (int i = 0; i < numbers.Length - 1; i++)
                for (int j = i + 1; j < numbers.Length; j++)
                    if (numbers[i] == numbers[j]) return true;
            return false;
        }
    }
}
