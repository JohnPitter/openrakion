using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    public sealed partial class WorldServer
    {
        public async Task<(byte Status, PendingEnchant? Pending)> PrepareEnchantAsync(
            ClientSession session, EnchantSelection selection)
        {
            byte status = ValidateEnchantSelection(selection, EnchantConfig, out int j1,
                out int j2, out int j3);
            if (status != 0) return (status, null);

            EnchantConfig config = EnchantConfig;
            double chance = config.SuccessChance(selection.Catalyst.ItemId,
                selection.TargetLevel, j1, j2, j3, session.PuActive);
            byte result = config.UseOriginalOutcomes
                ? EnchantRules.RollOriginal(config.OriginalOutcomeProbabilities(
                    selection.Catalyst.ItemId, selection.TargetLevel,
                    j1, j2, j3, session.PuActive), NextOriginalRoll(), selection.TargetLevel)
                : EnchantRules.Roll(chance, config.DowngradeFactor(selection.TargetLevel),
                    NextRoll(), NextRoll());
            int newLevel = Math.Clamp(
                selection.TargetLevel + EnchantRules.Delta(result), 0, 15);
            uint[]? serials = await _db.LoadEnchantSerialsAsync(selection);
            if (serials == null) return (7, null);
            return (0, new PendingEnchant(selection, result, newLevel, chance,
                config.Version, serials, Guid.NewGuid().ToString("D")));
        }

        public async Task<EnchantCommitResult> CommitEnchantAsync(
            ClientSession session, PendingEnchant pending)
        {
            if (session.GameInfoId <= 0 || pending.Selection.UserId != session.GameInfoId)
                return new EnchantCommitResult(false);
            var gate = _characterLocks.GetOrAdd(
                session.GameInfoId, _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                EnchantCommitResult result = await _db.CommitEnchantAsync(pending);
                if (!result.Success) return result;
                var storage = await _db.LoadStorageItemsAsync(session.GameInfoId);
                await session.ReplaceStorageAfterEnchantAsync(storage);
                Log.Ok("enchant", "[{0}] commit op={1} alvo={2} +{3}->+{4} result={5}",
                    session.Slot, pending.OperationId, pending.Selection.Target.RowId,
                    pending.Selection.TargetLevel, result.NewLevel, pending.Result);
                return result;
            }
            finally
            {
                gate.Release();
            }
        }

        private static byte ValidateEnchantSelection(
            EnchantSelection selection, EnchantConfig config,
            out int jewel1, out int jewel2, out int jewel3)
        {
            jewel1 = jewel2 = jewel3 = 0;
            if (selection.UserId <= 0 || selection.Target.RowId <= 0 ||
                selection.Target.ItemId <= 0 || selection.Target.ItemId >= 8000 ||
                selection.TargetLevel >= 15)
                return 6;
            if (!config.TryGetCatalyzer(selection.Catalyst.ItemId, out var catalyst) ||
                selection.Catalyst.RowId <= 0)
                return 7;
            if (selection.TargetLevel > catalyst.LevelCap) return 8;
            foreach (EnchantItemRef material in selection.Materials)
            {
                if (material.RowId <= 0) return 7;
                if (material.ItemId == 0x36b1) jewel1++;
                else if (material.ItemId == 0x36b2) jewel2++;
                else if (material.ItemId == 0x36b3) jewel3++;
                else return 7;
            }
            return 0;
        }

        private static double NextRoll() =>
            RandomNumberGenerator.GetInt32(1 << 24) / (double)(1 << 24);

        private static float NextOriginalRoll() =>
            EnchantRules.OriginalRollValue(RandomNumberGenerator.GetInt32(32768));
    }
}
