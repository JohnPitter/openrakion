using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Database
{
    public sealed partial class WorldDatabase
    {
        public async Task<StoragePurchaseResult> PurchaseStorageAsync(StoragePurchaseCommand request)
        {
            if (request.UserId <= 0 || request.CharacterId <= 0 ||
                request.CatalogItemId <= 0 || string.IsNullOrEmpty(request.AccountId) ||
                request.BasePrice <= 0 ||
                request.StorageCapacity is < 1 or > 120 || request.Grants.Count == 0)
                return new StoragePurchaseResult(StorageMutationStatus.Failed);
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                PaymentResolution payment = await ResolvePaymentAsync(connection, transaction,
                    new PaymentQuoteRequest(request.UserId, request.Payment,
                        request.BasePrice, !request.PayGold));
                if (payment.FailureStatus != 0)
                    return new StoragePurchaseResult(
                        (StorageMutationStatus)payment.FailureStatus);
                int balance = await LockWalletAsync(
                    connection, transaction, request.UserId, request.AccountId, request.PayGold);
                if (balance < payment.Cost)
                    return new StoragePurchaseResult(StorageMutationStatus.InsufficientFunds, balance);
                return await CommitStoragePurchaseAsync(connection, transaction,
                    new StoragePurchaseState(request, payment, balance));
            }
            catch (Exception ex)
            {
                Log.Error("shop", "compra transacional user={0}: {1}",
                    request.UserId, ex.Message);
                return new StoragePurchaseResult(StorageMutationStatus.Failed);
            }
        }

        private sealed record StoragePurchaseState(
            StoragePurchaseCommand Request, PaymentResolution Payment, int Balance);

        private static async Task<StoragePurchaseResult> CommitStoragePurchaseAsync(
            MySqlConnection connection, MySqlTransaction transaction, StoragePurchaseState state)
        {
            StoragePurchaseCommand request = state.Request;
            if (!await StorageCellsAvailableAsync(connection, transaction, request.UserId,
                    request.StorageCapacity, request.Grants))
                return new StoragePurchaseResult(StorageMutationStatus.NoSpace, state.Balance);

            int[] rows = new int[request.Grants.Count];
            int newBalance = state.Balance - state.Payment.Cost;
            long? couponLogId = await ConsumeCouponAsync(
                connection, transaction, request.UserId, state.Payment, request.CatalogItemId);
            for (int i = 0; i < request.Grants.Count; i++)
            {
                StorageGrant grant = request.Grants[i];
                int slot = grant.Cell ?? await FindFirstStorageSlotAsync(
                    connection, transaction, request.UserId, request.StorageCapacity);
                rows[i] = await InsertInventoryItemAsync(
                    connection, transaction, request.UserId, grant.ItemId, slot);
                long ledgerId = await WriteEconomyLedgerAsync(connection, transaction,
                    new EconomyLedgerEntry(request.UserId, request.CharacterId, grant.ItemId,
                        state.Payment.Cost, state.Balance, newBalance, !request.PayGold,
                        CouponLogId: couponLogId?.ToString() ?? ""));
                await SetPurchasedItemSerialAsync(connection, transaction, rows[i],
                    ledgerId, StorageEconomyRules.PurchaseSerialType(request.PayGold));
            }
            await UpdateWalletAsync(connection, transaction, request.UserId,
                request.AccountId, request.PayGold, newBalance);
            int[] presents = request.PayGold ? [] : await GrantRandomPresentAsync(
                connection, transaction, request.UserId, request.CharacterClass,
                state.Payment.Cost, request.Payment.UsesCoupon);
            await transaction.CommitAsync();
            return new StoragePurchaseResult(StorageMutationStatus.Success, newBalance,
                [.. request.Grants], rows, presents, state.Payment.CouponRowId,
                state.Payment.CouponItemId, state.Payment.LoggedDiscount);
        }

        public async Task<StorageSaleResult> SellStorageAsync(StorageSaleCommand request)
        {
            if (request.UserId <= 0 || request.CharacterId <= 0 || request.RowId <= 0 ||
                request.ItemId <= 0 || request.Cell is < 0 or >= 120 || request.GoldCredit < 0)
                return new StorageSaleResult(StorageMutationStatus.Failed);
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                int previousGold;
                await using (var wallet = new MySqlCommand(
                    "SELECT gold FROM usergameinfo WHERE id=@u FOR UPDATE", connection, transaction))
                {
                    wallet.Parameters.AddWithValue("@u", request.UserId);
                    object? value = await wallet.ExecuteScalarAsync();
                    if (value == null) return new StorageSaleResult(StorageMutationStatus.Failed);
                    previousGold = Convert.ToInt32(value);
                }
                StorageSaleItem? soldItem = await LockStorageSaleItemAsync(
                    connection, transaction, request);
                if (soldItem == null)
                    return new StorageSaleResult(StorageMutationStatus.Failed);

                string deleteSql = request.RemoveStack
                    ? "DELETE FROM useriteminfo WHERE userid=@u AND characterid=0 " +
                      "AND itemid=@item AND slot=@cell"
                    : "DELETE FROM useriteminfo WHERE id=@row AND userid=@u " +
                      "AND characterid=0 AND itemid=@item";
                int removed;
                await using (var delete = new MySqlCommand(deleteSql, connection, transaction))
                {
                    delete.Parameters.AddWithValue("@u", request.UserId);
                    delete.Parameters.AddWithValue("@item", request.ItemId);
                    delete.Parameters.AddWithValue("@cell", request.Cell);
                    delete.Parameters.AddWithValue("@row", request.RowId);
                    removed = await delete.ExecuteNonQueryAsync();
                }
                if (removed == 0) return new StorageSaleResult(StorageMutationStatus.Failed);

                await using (var credit = new MySqlCommand(
                    "UPDATE usergameinfo SET gold=gold+@gold WHERE id=@u", connection, transaction))
                {
                    credit.Parameters.AddWithValue("@gold", request.GoldCredit);
                    credit.Parameters.AddWithValue("@u", request.UserId);
                    await credit.ExecuteNonQueryAsync();
                }
                int gold = checked(previousGold + request.GoldCredit);
                await WriteEconomyLedgerAsync(connection, transaction,
                    new EconomyLedgerEntry(request.UserId, request.CharacterId,
                        request.ItemId, request.GoldCredit, previousGold, gold, false,
                        EconomyLedgerKind.Sale, soldItem.Level, soldItem.Experience));
                await transaction.CommitAsync();
                return new StorageSaleResult(StorageMutationStatus.Success, gold,
                    request.GoldCredit, request.RowId,
                    (byte)System.Math.Clamp(soldItem.Level, 0, byte.MaxValue),
                    soldItem.Experience);
            }
            catch (Exception ex)
            {
                Log.Error("shop", "venda transacional user={0} row={1}: {2}",
                    request.UserId, request.RowId, ex.Message);
                return new StorageSaleResult(StorageMutationStatus.Failed);
            }
        }

        private static async Task<int> LockWalletAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            int userId, string accountId, bool gold)
        {
            string sql = gold
                ? "SELECT gold FROM usergameinfo WHERE id=@id FOR UPDATE"
                : "SELECT cash FROM cash WHERE id=@account FOR UPDATE";
            await using var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@id", userId);
            command.Parameters.AddWithValue("@account", accountId);
            object? value = await command.ExecuteScalarAsync();
            return value == null ? -1 : Convert.ToInt32(value);
        }

        private sealed record StorageSaleItem(int Level, long Experience);

        private static async Task<StorageSaleItem?> LockStorageSaleItemAsync(
            MySqlConnection connection, MySqlTransaction transaction, StorageSaleCommand request)
        {
            await using var command = new MySqlCommand(
                "SELECT level,exp FROM useriteminfo WHERE id=@row AND userid=@u " +
                "AND characterid=0 AND itemid=@item LIMIT 1 FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("@row", request.RowId);
            command.Parameters.AddWithValue("@u", request.UserId);
            command.Parameters.AddWithValue("@item", request.ItemId);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync()
                ? new StorageSaleItem(reader.GetInt32(0), reader.GetInt64(1))
                : null;
        }

        private static async Task UpdateWalletAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            int userId, string accountId, bool gold, int balance)
        {
            string sql = gold
                ? "UPDATE usergameinfo SET gold=@balance WHERE id=@id"
                : "UPDATE cash SET cash=@balance WHERE id=@account";
            await using var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@balance", balance);
            command.Parameters.AddWithValue("@id", userId);
            command.Parameters.AddWithValue("@account", accountId);
            if (await command.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("Carteira mudou durante a compra.");
        }

        private static async Task SetPurchasedItemSerialAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            int rowId, long ledgerId, byte serialType)
        {
            if (ledgerId <= 0)
                throw new InvalidOperationException("Ledger sem identidade para o serial do item.");
            await using var command = new MySqlCommand(
                "UPDATE useriteminfo SET item_sn=@serial,sn_type=@type WHERE id=@id",
                connection, transaction);
            command.Parameters.AddWithValue("@serial", ledgerId);
            command.Parameters.AddWithValue("@type", serialType);
            command.Parameters.AddWithValue("@id", rowId);
            if (await command.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("Falha ao vincular item ao ledger da compra.");
        }

        private static async Task<bool> StorageCellsAvailableAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            int userId, int storageCapacity, IReadOnlyList<StorageGrant> grants)
        {
            foreach (StorageGrant grant in grants)
            {
                if (grant.Cell == null) continue;
                if (grant.Cell < 0 || grant.Cell >= storageCapacity) return false;
                await using var command = new MySqlCommand(
                    "SELECT itemid FROM useriteminfo WHERE userid=@u AND characterid=0 " +
                    "AND slot=@cell FOR UPDATE",
                    connection, transaction);
                command.Parameters.AddWithValue("@u", userId);
                command.Parameters.AddWithValue("@cell", grant.Cell.Value);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    if (reader.GetInt32(0) != grant.ItemId || grant.ItemId is < 12000 or > 12999)
                        return false;
            }
            return true;
        }

        private static async Task<int> FindFirstStorageSlotAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            int userId, int storageCapacity)
        {
            var occupied = new bool[storageCapacity];
            await using var command = new MySqlCommand(
                "SELECT slot FROM useriteminfo WHERE userid=@u AND characterid=0 FOR UPDATE",
                connection, transaction);
            command.Parameters.AddWithValue("@u", userId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int slot = reader.GetInt32(0);
                if (slot >= 0 && slot < storageCapacity) occupied[slot] = true;
            }
            int free = Array.FindIndex(occupied, value => !value);
            if (free < 0) throw new InvalidOperationException("Storage sem célula livre.");
            return free;
        }
    }
}
