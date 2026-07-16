using System;
using System.Data;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Database
{
    public sealed partial class WorldDatabase
    {
        public async Task<StageLevelFreeResult> PurchaseStageLevelFreeAsync(
            int userId, string accountId, int characterId)
        {
            if (userId <= 0 || characterId <= 0 || string.IsNullOrWhiteSpace(accountId))
                return new StageLevelFreeResult(StageEntitlementStatus.Failed);

            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
                StageLevelFreeAccount? account = await LoadStageLevelFreeAccountAsync(
                    connection, transaction, userId, accountId, characterId);
                if (account == null)
                    return await RollbackStageLevelFreeAsync(transaction, StageEntitlementStatus.Failed);

                ItemOffer? offer = await LoadItemOfferAsync(
                    connection, transaction, InventoryEntitlementRules.StageLevelFreeProduct);
                if (offer == null || !offer.PayCash || offer.Price <= 0)
                    return await RollbackStageLevelFreeAsync(
                        transaction, StageEntitlementStatus.Failed, account);
                if (account.Cash < offer.Price)
                    return await RollbackStageLevelFreeAsync(
                        transaction, StageEntitlementStatus.InsufficientCash, account);
                if (!InventoryEntitlementRules.CanPurchaseStageLevelFree(
                        account.MinuteMarker, account.NowMarker))
                    return await RollbackStageLevelFreeAsync(
                        transaction, StageEntitlementStatus.NotEligible, account);

                int changed = await ExecuteCountAsync(connection, transaction,
                    "UPDATE usergameinfo SET stagelevelfree=@now " +
                    "WHERE id=@u AND stagelevelfree=@current",
                    ("@now", account.NowMarker), ("@u", userId),
                    ("@current", account.MinuteMarker));
                if (changed != 1)
                    throw new InvalidOperationException("Stage Level Free mudou durante a compra.");

                int nextCash = account.Cash - offer.Price;
                await UpdateWalletAsync(connection, transaction, userId, accountId, false, nextCash);
                await WriteEconomyLedgerAsync(connection, transaction,
                    new EconomyLedgerEntry(userId, characterId,
                        InventoryEntitlementRules.StageLevelFreeProduct, offer.Price,
                        account.Cash, nextCash, true));
                int[] presents = await GrantRandomPresentAsync(connection, transaction, userId,
                    account.CharacterClass, offer.Price, false);
                await transaction.CommitAsync();
                return new StageLevelFreeResult(StageEntitlementStatus.Success,
                    account.Gold, nextCash, checked((uint)account.NowMarker), presents);
            }
            catch (Exception ex)
            {
                Log.Error("shop", "PurchaseStageLevelFreeAsync(user={0}, char={1}): {2}",
                    userId, characterId, ex.Message);
                return new StageLevelFreeResult(StageEntitlementStatus.Failed);
            }
        }

        private sealed record StageLevelFreeAccount(
            int Gold, int Cash, long MinuteMarker, long NowMarker, byte CharacterClass);

        private static async Task<StageLevelFreeAccount?> LoadStageLevelFreeAccountAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId,
            string accountId, int characterId)
        {
            await using var command = new MySqlCommand(
                "SELECT g.gold,ca.cash,g.stagelevelfree," +
                "((TO_DAYS(NOW())*24+HOUR(NOW()))*60+MINUTE(NOW())),ch.class " +
                "FROM usergameinfo g JOIN cash ca ON ca.id=@account " +
                "JOIN characterinfo ch ON ch.id=@char AND ch.userid=g.id " +
                "WHERE g.id=@u LIMIT 1 FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("@account", accountId);
            command.Parameters.AddWithValue("@char", characterId);
            command.Parameters.AddWithValue("@u", userId);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync()
                ? new StageLevelFreeAccount(reader.GetInt32(0), reader.GetInt32(1),
                    reader.GetInt64(2), reader.GetInt64(3), checked((byte)reader.GetInt32(4)))
                : null;
        }

        private static async Task<StageLevelFreeResult> RollbackStageLevelFreeAsync(
            MySqlTransaction transaction, StageEntitlementStatus status,
            StageLevelFreeAccount? account = null)
        {
            await transaction.RollbackAsync();
            return new StageLevelFreeResult(status, account?.Gold ?? 0, account?.Cash ?? 0,
                checked((uint)(account?.MinuteMarker ?? 0)));
        }
    }
}
