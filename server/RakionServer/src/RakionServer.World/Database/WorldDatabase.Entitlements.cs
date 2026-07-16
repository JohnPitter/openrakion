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
        public async Task<EntitlementPurchaseResult> PurchaseEntitlementAsync(
            int userId, string accountId, int characterId,
            InventoryEntitlement entitlement, CharacterPayment payment)
        {
            if (userId <= 0 || characterId <= 0 || string.IsNullOrWhiteSpace(accountId))
                return new EntitlementPurchaseResult(EntitlementPurchaseStatus.Failed);

            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
                int basePrice = InventoryEntitlementRules.Price(entitlement);
                PaymentResolution resolved = await ResolvePaymentAsync(connection, transaction,
                    new PaymentQuoteRequest(userId, payment, basePrice, true));
                if (resolved.FailureStatus != 0)
                    return await RollbackEntitlementAsync(
                        transaction, (EntitlementPurchaseStatus)resolved.FailureStatus);

                EntitlementAccount? account = await LoadEntitlementAccountAsync(
                    connection, transaction, userId, accountId, characterId);
                if (account == null)
                    return await RollbackEntitlementAsync(transaction, EntitlementPurchaseStatus.Failed);
                byte current = entitlement == InventoryEntitlement.Bag ? account.Bag : account.CharacterSlots;
                if (current >= InventoryEntitlementRules.Maximum(entitlement))
                    return await RollbackEntitlementAsync(
                        transaction, EntitlementPurchaseStatus.LimitReached, account);
                if (account.Cash < resolved.Cost)
                    return await RollbackEntitlementAsync(
                        transaction, EntitlementPurchaseStatus.InsufficientCash, account);

                byte next = checked((byte)(current + 1));
                int nextCash = account.Cash - resolved.Cost;
                string column = entitlement == InventoryEntitlement.Bag ? "bag" : "slot";
                int changed = await ExecuteCountAsync(connection, transaction,
                    $"UPDATE usergameinfo SET `{column}`=@next WHERE id=@u AND `{column}`=@current",
                    ("@next", next), ("@u", userId), ("@current", current));
                if (changed != 1)
                    throw new InvalidOperationException(
                        $"Expansao mudou durante a compra (atual={current}, proximo={next}, linhas={changed}).");
                await UpdateWalletAsync(connection, transaction, userId, accountId, false, nextCash);

                int productId = InventoryEntitlementRules.ProductId(entitlement);
                long? couponLogId = await ConsumeCouponAsync(
                    connection, transaction, userId, resolved, productId);
                await WriteEconomyLedgerAsync(connection, transaction,
                    new EconomyLedgerEntry(userId, characterId, productId, resolved.Cost,
                        account.Cash, nextCash, true,
                        CouponLogId: couponLogId?.ToString() ?? ""));
                int[] presents = await GrantRandomPresentAsync(connection, transaction, userId,
                    account.CharacterClass, resolved.Cost, payment.UsesCoupon);
                await transaction.CommitAsync();
                return new EntitlementPurchaseResult(EntitlementPurchaseStatus.Success,
                    account.Gold, nextCash, next, resolved.CouponItemId, presents);
            }
            catch (Exception ex)
            {
                Log.Error("shop", "PurchaseEntitlementAsync(user={0}, type={1}): {2}",
                    userId, entitlement, ex.Message);
                return new EntitlementPurchaseResult(EntitlementPurchaseStatus.Failed);
            }
        }

        public async Task<PotionSlotPurchaseResult> PurchasePotionSlotAsync(
            int userId, string accountId, int characterId)
        {
            if (userId <= 0 || characterId <= 0 || string.IsNullOrWhiteSpace(accountId))
                return new PotionSlotPurchaseResult(EntitlementPurchaseStatus.Failed);
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
                PotionPurchaseAccount? account = await LoadPotionPurchaseAccountAsync(
                    connection, transaction, userId, accountId, characterId);
                if (account == null)
                    return await RollbackPotionSlotAsync(transaction, EntitlementPurchaseStatus.Failed);
                int productId = InventoryEntitlementRules.PotionSlotProduct(account.PotionSlots);
                if (productId == 0)
                    return await RollbackPotionSlotAsync(
                        transaction, EntitlementPurchaseStatus.LimitReached, account);
                ItemOffer? offer = await LoadItemOfferAsync(
                    connection, transaction, productId);
                if (offer == null || offer.Price <= 0)
                    return await RollbackPotionSlotAsync(
                        transaction, EntitlementPurchaseStatus.Failed, account);
                int balance = offer.PayCash ? account.Cash : account.Gold;
                if (balance < offer.Price)
                    return await RollbackPotionSlotAsync(
                        transaction, EntitlementPurchaseStatus.InsufficientCash, account);

                byte next = checked((byte)(account.PotionSlots + 1));
                int changed = await ExecuteCountAsync(connection, transaction,
                    "UPDATE characterinfo SET potionslot=@next " +
                    "WHERE id=@char AND userid=@u AND potionslot=@current",
                    ("@next", next), ("@char", characterId), ("@u", userId),
                    ("@current", account.PotionSlots));
                if (changed != 1)
                    throw new InvalidOperationException("Potion slot mudou durante a compra.");
                int nextBalance = balance - offer.Price;
                await UpdateWalletAsync(connection, transaction, userId, accountId,
                    !offer.PayCash, nextBalance);
                await WriteEconomyLedgerAsync(connection, transaction,
                    new EconomyLedgerEntry(userId, characterId, productId, offer.Price,
                        balance, nextBalance, offer.PayCash));
                int[] presents = offer.PayCash
                    ? await GrantRandomPresentAsync(connection, transaction, userId,
                        account.CharacterClass, offer.Price, false)
                    : [];
                await transaction.CommitAsync();
                return new PotionSlotPurchaseResult(EntitlementPurchaseStatus.Success,
                    offer.PayCash ? account.Gold : nextBalance,
                    offer.PayCash ? nextBalance : account.Cash, next, presents);
            }
            catch (Exception ex)
            {
                Log.Error("shop", "PurchasePotionSlotAsync(user={0}, char={1}): {2}",
                    userId, characterId, ex.Message);
                return new PotionSlotPurchaseResult(EntitlementPurchaseStatus.Failed);
            }
        }

        public async Task<StageRankClearResult> PurchaseStageRankClearAsync(
            int userId, string accountId, int characterId)
        {
            if (userId <= 0 || characterId <= 0 || string.IsNullOrWhiteSpace(accountId))
                return new StageRankClearResult(StageEntitlementStatus.Failed);
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
                StagePurchaseAccount? account = await LoadStagePurchaseAccountAsync(
                    connection, transaction, userId, accountId, characterId);
                if (account == null)
                    return await RollbackStageRankClearAsync(transaction, StageEntitlementStatus.Failed);
                int productId = InventoryEntitlementRules.StageRankClearProduct(account.Level);
                if (productId == 0)
                    return await RollbackStageRankClearAsync(
                        transaction, StageEntitlementStatus.NotEligible, account);
                ItemOffer? offer = await LoadItemOfferAsync(connection, transaction, productId);
                if (offer == null || !offer.PayCash || offer.Price <= 0)
                    return await RollbackStageRankClearAsync(
                        transaction, StageEntitlementStatus.Failed, account);
                if (account.Cash < offer.Price)
                    return await RollbackStageRankClearAsync(
                        transaction, StageEntitlementStatus.InsufficientCash, account);

                int nextCash = account.Cash - offer.Price;
                await ExecuteAsync(connection, transaction,
                    "DELETE FROM userstageinfo WHERE characterid=@char", ("@char", characterId));
                await UpdateWalletAsync(connection, transaction, userId, accountId, false, nextCash);
                await WriteEconomyLedgerAsync(connection, transaction,
                    new EconomyLedgerEntry(userId, characterId, productId, offer.Price,
                        account.Cash, nextCash, true));
                int[] presents = await GrantRandomPresentAsync(connection, transaction, userId,
                    account.CharacterClass, offer.Price, false);
                await transaction.CommitAsync();
                return new StageRankClearResult(
                    StageEntitlementStatus.Success, account.Gold, nextCash, presents);
            }
            catch (Exception ex)
            {
                Log.Error("shop", "PurchaseStageRankClearAsync(user={0}, char={1}): {2}",
                    userId, characterId, ex.Message);
                return new StageRankClearResult(StageEntitlementStatus.Failed);
            }
        }

        private sealed record EntitlementAccount(
            int Gold, int Cash, byte Bag, byte CharacterSlots, byte CharacterClass);

        private sealed record PotionPurchaseAccount(
            int Gold, int Cash, byte PotionSlots, byte CharacterClass);

        private sealed record StagePurchaseAccount(
            int Gold, int Cash, byte Level, byte CharacterClass);

        private sealed record ItemOffer(bool PayCash, int Price);

        private static async Task<EntitlementAccount?> LoadEntitlementAccountAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId,
            string accountId, int characterId)
        {
            await using var command = new MySqlCommand(
                "SELECT g.gold,ca.cash,g.bag,g.slot,ch.class FROM usergameinfo g " +
                "JOIN cash ca ON ca.id=@account JOIN characterinfo ch ON ch.id=@char AND ch.userid=g.id " +
                "WHERE g.id=@u LIMIT 1 FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("@account", accountId);
            command.Parameters.AddWithValue("@char", characterId);
            command.Parameters.AddWithValue("@u", userId);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync()
                ? new EntitlementAccount(reader.GetInt32(0), reader.GetInt32(1),
                    checked((byte)reader.GetInt32(2)), checked((byte)reader.GetInt32(3)),
                    checked((byte)reader.GetInt32(4)))
                : null;
        }

        private static async Task<PotionPurchaseAccount?> LoadPotionPurchaseAccountAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId,
            string accountId, int characterId)
        {
            await using var command = new MySqlCommand(
                "SELECT g.gold,ca.cash,ch.potionslot,ch.class FROM characterinfo ch " +
                "JOIN usergameinfo g ON g.id=ch.userid JOIN cash ca ON ca.id=@account " +
                "WHERE ch.id=@char AND ch.userid=@u LIMIT 1 FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("@account", accountId);
            command.Parameters.AddWithValue("@char", characterId);
            command.Parameters.AddWithValue("@u", userId);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync()
                ? new PotionPurchaseAccount(reader.GetInt32(0), reader.GetInt32(1),
                    checked((byte)reader.GetInt32(2)), checked((byte)reader.GetInt32(3)))
                : null;
        }

        private static async Task<StagePurchaseAccount?> LoadStagePurchaseAccountAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId,
            string accountId, int characterId)
        {
            await using var command = new MySqlCommand(
                "SELECT g.gold,ca.cash,ch.level,ch.class FROM characterinfo ch " +
                "JOIN usergameinfo g ON g.id=ch.userid JOIN cash ca ON ca.id=@account " +
                "WHERE ch.id=@char AND ch.userid=@u LIMIT 1 FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("@account", accountId);
            command.Parameters.AddWithValue("@char", characterId);
            command.Parameters.AddWithValue("@u", userId);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync()
                ? new StagePurchaseAccount(reader.GetInt32(0), reader.GetInt32(1),
                    checked((byte)reader.GetInt32(2)), checked((byte)reader.GetInt32(3)))
                : null;
        }

        private static async Task<ItemOffer?> LoadItemOfferAsync(
            MySqlConnection connection, MySqlTransaction transaction, int productId)
        {
            await using var command = new MySqlCommand(
                "SELECT shop,gold,cash FROM iteminfo WHERE id=@item LIMIT 1 FOR UPDATE",
                connection, transaction);
            command.Parameters.AddWithValue("@item", productId);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            int shop = reader.GetInt32(0);
            return shop switch
            {
                1 => new ItemOffer(false, reader.GetInt32(1)),
                2 => new ItemOffer(true, reader.GetInt32(2)),
                _ => null
            };
        }

        private static async Task<int> ExecuteCountAsync(
            MySqlConnection connection, MySqlTransaction transaction, string sql,
            params (string Name, object Value)[] parameters)
        {
            await using var command = new MySqlCommand(sql, connection, transaction);
            foreach (var parameter in parameters)
                command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            return await command.ExecuteNonQueryAsync();
        }

        private static async Task<EntitlementPurchaseResult> RollbackEntitlementAsync(
            MySqlTransaction transaction, EntitlementPurchaseStatus status,
            EntitlementAccount? account = null)
        {
            await transaction.RollbackAsync();
            return new EntitlementPurchaseResult(status, account?.Gold ?? 0, account?.Cash ?? 0);
        }

        private static async Task<PotionSlotPurchaseResult> RollbackPotionSlotAsync(
            MySqlTransaction transaction, EntitlementPurchaseStatus status,
            PotionPurchaseAccount? account = null)
        {
            await transaction.RollbackAsync();
            return new PotionSlotPurchaseResult(
                status, account?.Gold ?? 0, account?.Cash ?? 0, account?.PotionSlots ?? 0);
        }

        private static async Task<StageRankClearResult> RollbackStageRankClearAsync(
            MySqlTransaction transaction, StageEntitlementStatus status,
            StagePurchaseAccount? account = null)
        {
            await transaction.RollbackAsync();
            return new StageRankClearResult(status, account?.Gold ?? 0, account?.Cash ?? 0);
        }
    }
}
