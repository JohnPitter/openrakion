using System;
using System.Security.Cryptography;

namespace RakionServer.World.Domain
{
    public static class CharacterEconomyRules
    {
        private static readonly int[] CommonThresholds = [1000, 4000, 40000, 72000, 102667, 153333];
        private static readonly int[] RareThresholds = [230, 857, 8571, 15429, 22000, 32857];

        public readonly record struct CouponQuote(int FinalCost, int LoggedDiscount);

        public static int StateClearCost(int level) => level < 16 ? 7000 : level < 41 ? 12000 : 19000;

        public static int StateClearProduct(int level) => level < 16 ? 10002 : level < 41 ? 10003 : 10004;

        public static int PowerUserProduct(byte mode) => mode switch
        {
            0 => 10000,
            1 => 10001,
            _ => 0
        };

        public static CouponQuote ApplyCoupon(int baseCost, int discountRate)
        {
            if (baseCost < 0) throw new ArgumentOutOfRangeException(nameof(baseCost));
            if (discountRate is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(discountRate));
            int rawDiscount = baseCost * discountRate / 100;
            int finalCost = (baseCost - rawDiscount) / 100 * 100;
            int loggedDiscount = (rawDiscount + 99) / 100 * 100;
            return new CouponQuote(finalCost, loggedDiscount);
        }

        public static int LegacyPresentRoll(int legacyRandom) =>
            checked((int)(((long)legacyRandom + 1) * (legacyRandom + 1) % 1_000_000));

        public static int? SelectPresent(int cashCost, byte characterClass, int roll, int variant)
        {
            if (cashCost <= 0 || characterClass > 4 || roll is < 0 or >= 1_000_000 || variant is < 0 or > 3)
                return null;

            int grade = Math.Min(5, (cashCost - 1) / 5000);
            int rewardTier = -1;
            if (roll < CommonThresholds[grade]) rewardTier = 1;
            if (roll < RareThresholds[grade]) rewardTier = 0;
            if (rewardTier < 0) return null;

            int classBase = (characterClass + 1) * 1000;
            return classBase + (rewardTier == 0 ? 240 : 40) + variant;
        }

        public static int? RollPresent(int cashCost, byte characterClass)
        {
            int legacyRandom = RandomNumberGenerator.GetInt32(32768);
            int roll = LegacyPresentRoll(legacyRandom);
            int variant = RandomNumberGenerator.GetInt32(4);
            return SelectPresent(cashCost, characterClass, roll, variant);
        }
    }
}
