using System;
using System.Collections.Generic;

namespace RakionServer.World.Domain
{
    public sealed record EnchantItemRef(byte Slot, int RowId, int ItemId);

    public sealed record EnchantSelection(
        int UserId, EnchantItemRef Target, int TargetLevel,
        EnchantItemRef Catalyst, IReadOnlyList<EnchantItemRef> Materials);

    public sealed record PendingEnchant(
        EnchantSelection Selection, byte Result, int NewLevel,
        double Chance, int ConfigVersion, uint[] Serials, string OperationId);

    public sealed record EnchantCommitResult(bool Success, int NewLevel = 0);

    public static class EnchantRules
    {
        public static byte ValidateSlots(
            byte target, byte catalyst, IReadOnlyList<byte> materials)
        {
            if (materials.Count > 3 || target == catalyst) return 7;
            var slots = new HashSet<byte> { target, catalyst };
            foreach (byte material in materials)
                if (!slots.Add(material)) return 7;
            return 0;
        }

        public static byte Roll(double successChance, double downgradeFactor,
            double successRoll, double downgradeRoll)
        {
            if (successRoll < successChance) return 0;
            double downgradeChance = (1.0 - successChance) * downgradeFactor;
            return downgradeRoll < downgradeChance ? (byte)2 : (byte)1;
        }

        public static byte RollOriginal(
            IReadOnlyList<float> probabilities, float roll, int currentLevel)
        {
            int count = Math.Min(6, probabilities.Count);
            for (byte result = 0; result < count; result++)
            {
                float probability = Math.Max(0.0f, probabilities[result]);
                if (roll < probability)
                    return result == 5 ? (currentLevel < 4 ? (byte)4 : (byte)1) : result;
                roll -= probability;
            }
            return 6;
        }

        public static int Delta(byte result) => result switch
        {
            0 => 1,
            2 => -1,
            3 => -2,
            4 => -3,
            _ => 0
        };

        public static float OriginalRollValue(int sample)
        {
            if (sample is < 0 or > 32767)
                throw new ArgumentOutOfRangeException(nameof(sample));
            return sample / 32767.0f;
        }
    }
}
