using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.World.Database
{
    public sealed partial class WorldDatabase
    {
        public async Task<int> UnpackSetInStorageAsync(
            int userId, StorageItem setItem, IReadOnlyList<int> members, int storageCapacity)
        {
            if (members.Count == 0 || storageCapacity is < 1 or > 120) return 0;
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                StoredItem? set = await LoadStoredItemAsync(
                    connection, transaction, userId, setItem.Id, setItem.ItemId);
                if (set == null) return 0;
                if (set.Slot < 0 || set.Slot >= storageCapacity)
                    throw new InvalidOperationException("Set está fora da capacidade do storage.");

                HashSet<int> occupied = await LoadStorageSlotsAsync(
                    connection, transaction, userId);
                occupied.Remove(set.Slot);
                await DeleteInventoryItemAsync(connection, transaction, set.Id, userId);
                int slot = set.Slot;
                for (int index = 0; index < members.Count; index++)
                {
                    await InsertInventoryItemAsync(connection, transaction,
                        userId, members[index], slot, set.Level, set.LimitTime);
                    occupied.Add(slot);
                    if (index + 1 < members.Count)
                    {
                        slot = FindNextStorageSlot(0, occupied);
                        if (slot >= storageCapacity)
                            throw new InvalidOperationException("Storage sem espaço para desempacotar o set.");
                    }
                }
                await transaction.CommitAsync();
                Log.Ok("shop", "set {0} desempacotado no useriteminfo (user {1}): {2} peças",
                    setItem.ItemId, userId, members.Count);
                return members.Count;
            }
            catch (Exception ex)
            {
                Log.Error("db", "UnpackSetInStorageAsync({0},{1}): {2}",
                    userId, setItem.Id, ex.Message);
                return 0;
            }
        }

        public async Task<List<StorageItem>> LoadStorageItemsAsync(int userId)
        {
            var items = new List<StorageItem>();
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var command = new MySqlCommand(
                    "SELECT id,itemid,level,slot FROM useriteminfo " +
                    "WHERE userid=@u AND characterid=0 AND " +
                    "(limittime=0 OR limittime>=((TO_DAYS(NOW())*24+HOUR(NOW()))*60+MINUTE(NOW()))) " +
                    "ORDER BY id", connection);
                command.Parameters.AddWithValue("@u", userId);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    items.Add(new StorageItem(reader.GetInt32(0), reader.GetInt32(1),
                        reader.GetInt32(2), reader.GetInt32(3)));
            }
            catch (Exception ex)
            {
                Log.Error("db", "LoadStorageItemsAsync({0}): {1}", userId, ex.Message);
            }
            return items;
        }

        public async Task<int> MoveInvalidItemsToStorageAsync(
            int userId, int characterId, IReadOnlyList<int> rowIds, int capacity)
        {
            if (rowIds.Count == 0) return 0;
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                HashSet<int> occupied = await LoadStorageSlotsAsync(
                    connection, transaction, userId);
                int moved = 0;
                foreach (int rowId in rowIds)
                {
                    int slot = FindNextStorageSlot(0, occupied);
                    if (slot >= capacity)
                        throw new InvalidOperationException("Storage sem espaço para equipamento incompatível.");
                    await using var command = new MySqlCommand(
                        "UPDATE useriteminfo SET characterid=0,slot=@slot " +
                        "WHERE id=@id AND userid=@u AND characterid=@char",
                        connection, transaction);
                    command.Parameters.AddWithValue("@slot", slot);
                    command.Parameters.AddWithValue("@id", rowId);
                    command.Parameters.AddWithValue("@u", userId);
                    command.Parameters.AddWithValue("@char", characterId);
                    if (await command.ExecuteNonQueryAsync() != 1)
                        throw new InvalidOperationException($"Item inválido {rowId} mudou durante o login.");
                    occupied.Add(slot);
                    moved++;
                }
                await transaction.CommitAsync();
                return moved;
            }
            catch (Exception ex)
            {
                Log.Error("db", "MoveInvalidItemsToStorageAsync({0},{1}): {2}",
                    userId, characterId, ex.Message);
                return -1;
            }
        }

        public async Task UpdateInventoryItemLevelAsync(int rowId, int level)
        {
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var command = new MySqlCommand(
                    "UPDATE useriteminfo SET level=@level WHERE id=@id", connection);
                command.Parameters.AddWithValue("@level", (byte)Math.Clamp(level, 0, 15));
                command.Parameters.AddWithValue("@id", rowId);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Log.Error("db", "UpdateInventoryItemLevelAsync({0},{1}): {2}",
                    rowId, level, ex.Message);
            }
        }

        public async Task DeleteInventoryItemAsync(int rowId)
        {
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var command = new MySqlCommand(
                    "DELETE FROM useriteminfo WHERE id=@id", connection);
                command.Parameters.AddWithValue("@id", rowId);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Log.Error("db", "DeleteInventoryItemAsync({0}): {1}", rowId, ex.Message);
            }
        }

        public async Task<List<(int Cell, int ItemId, int Count)>> LoadQuickslotAsync(
            int userId, int characterId)
        {
            var items = new List<(int, int, int)>();
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var command = new MySqlCommand(
                    "SELECT ui.slot,ui.itemid,COUNT(*) FROM useriteminfo ui " +
                    "JOIN iteminfo def ON def.id=ui.itemid AND def.type=12 " +
                    "WHERE ui.userid=@u AND ui.characterid=@char AND ui.slot BETWEEN 13 AND 18 " +
                    "AND (ui.limittime=0 OR ui.limittime>=((TO_DAYS(NOW())*24+HOUR(NOW()))*60+MINUTE(NOW()))) " +
                    "GROUP BY ui.slot,ui.itemid ORDER BY ui.slot", connection);
                command.Parameters.AddWithValue("@u", userId);
                command.Parameters.AddWithValue("@char", characterId);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    items.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)));
            }
            catch (Exception ex)
            {
                Log.Error("db", "LoadQuickslotAsync({0},{1}): {2}",
                    userId, characterId, ex.Message);
            }
            return items;
        }

        public async Task<FieldPotionConsumeResult> ConsumeFieldPotionAsync(
            FieldPotionConsumeRequest request)
        {
            if (request.UserId <= 0 || request.CharacterId <= 0 || request.Cell is < 13 or > 18 ||
                request.ItemId is < 12000 or > 12999)
                return new FieldPotionConsumeResult(false);

            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                int? rowId = await LockFieldPotionRowAsync(
                    connection, transaction, request);
                if (rowId == null) return new FieldPotionConsumeResult(false);

                await DeleteInventoryItemAsync(
                    connection, transaction, rowId.Value, request.UserId);
                int remaining = await CountFieldPotionAsync(
                    connection, transaction, request);
                await transaction.CommitAsync();
                Log.Ok("potion", "poção consumida (uid={0}, char={1}, slot={2}, item={3}, restante={4})",
                    request.UserId, request.CharacterId, request.Cell, request.ItemId, remaining);
                return new FieldPotionConsumeResult(true, remaining);
            }
            catch (Exception ex)
            {
                Log.Error("db", "ConsumeFieldPotionAsync({0},{1},{2},{3}): {4}",
                    request.UserId, request.CharacterId, request.Cell, request.ItemId, ex.Message);
                return new FieldPotionConsumeResult(false);
            }
        }

        private static async Task<int?> LockFieldPotionRowAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            FieldPotionConsumeRequest request)
        {
            await using var command = new MySqlCommand(
                "SELECT id FROM useriteminfo WHERE userid=@u AND characterid=@char " +
                "AND slot=@slot AND itemid=@item " +
                "AND (limittime=0 OR limittime>=((TO_DAYS(NOW())*24+HOUR(NOW()))*60+MINUTE(NOW()))) " +
                "ORDER BY id LIMIT 1 FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("@u", request.UserId);
            command.Parameters.AddWithValue("@char", request.CharacterId);
            command.Parameters.AddWithValue("@slot", request.Cell);
            command.Parameters.AddWithValue("@item", request.ItemId);
            object? value = await command.ExecuteScalarAsync();
            return value == null ? null : Convert.ToInt32(value);
        }

        private static async Task<int> CountFieldPotionAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            FieldPotionConsumeRequest request)
        {
            await using var command = new MySqlCommand(
                "SELECT COUNT(*) FROM useriteminfo WHERE userid=@u AND characterid=@char " +
                "AND slot=@slot AND itemid=@item " +
                "AND (limittime=0 OR limittime>=((TO_DAYS(NOW())*24+HOUR(NOW()))*60+MINUTE(NOW())))",
                connection, transaction);
            command.Parameters.AddWithValue("@u", request.UserId);
            command.Parameters.AddWithValue("@char", request.CharacterId);
            command.Parameters.AddWithValue("@slot", request.Cell);
            command.Parameters.AddWithValue("@item", request.ItemId);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public async Task<bool> SaveInventoryLayoutAsync(
            int userId, int characterId, IReadOnlyList<int> activeItems,
            IReadOnlyList<int> activeRowIds, IReadOnlyList<int> boxItems,
            IReadOnlyList<int> boxRowIds)
        {
            if (userId <= 0 || characterId <= 0) return false;
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                await LockInventoryAsync(connection, transaction, userId);
                await ResetPotionRowsAsync(connection, transaction, userId, characterId);
                await PersistActiveRowsAsync(connection, transaction,
                    userId, characterId, activeItems, activeRowIds);
                await PersistStorageRowsAsync(connection, transaction,
                    userId, boxItems, boxRowIds);
                await EnsureUniqueNonPotionStorageSlotsAsync(connection, transaction, userId);
                await transaction.CommitAsync();
                Log.Ok("shop", "layout useriteminfo persistido (uid={0}, char={1})",
                    userId, characterId);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("db", "SaveInventoryLayoutAsync({0},{1}): {2}",
                    userId, characterId, ex.Message);
                return false;
            }
        }

        private static async Task PersistActiveRowsAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId,
            int characterId, IReadOnlyList<int> items, IReadOnlyList<int> rowIds)
        {
            for (int slot = 0; slot < items.Count && slot < 19; slot++)
            {
                int itemId = items[slot];
                if (itemId == 0) continue;
                bool potion = itemId is >= 12000 and <= 12999;
                string sql = potion
                    ? "UPDATE useriteminfo SET characterid=@char,slot=@slot " +
                      "WHERE userid=@u AND itemid=@item " +
                      "AND (characterid=0 OR characterid=@char)"
                    : "UPDATE useriteminfo SET characterid=@char,slot=@slot " +
                      "WHERE id=@row AND userid=@u";
                await using var command = new MySqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("@char", characterId);
                command.Parameters.AddWithValue("@slot", slot);
                command.Parameters.AddWithValue("@u", userId);
                if (potion) command.Parameters.AddWithValue("@item", itemId);
                else command.Parameters.AddWithValue("@row", rowIds[slot]);
                if (await command.ExecuteNonQueryAsync() == 0)
                    throw new InvalidOperationException($"Item ativo ausente no slot {slot}.");
            }
        }

        private static async Task PersistStorageRowsAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId,
            IReadOnlyList<int> items, IReadOnlyList<int> rowIds)
        {
            for (int slot = 0; slot < items.Count && slot < 120; slot++)
            {
                int itemId = items[slot];
                if (itemId == 0) continue;
                bool potion = itemId is >= 12000 and <= 12999;
                string sql = potion
                    ? "UPDATE useriteminfo SET characterid=0,slot=@slot " +
                      "WHERE userid=@u AND itemid=@item AND characterid=0"
                    : "UPDATE useriteminfo SET characterid=0,slot=@slot " +
                      "WHERE id=@row AND userid=@u";
                await using var command = new MySqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("@slot", slot);
                command.Parameters.AddWithValue("@u", userId);
                if (potion) command.Parameters.AddWithValue("@item", itemId);
                else command.Parameters.AddWithValue("@row", rowIds[slot]);
                if (await command.ExecuteNonQueryAsync() == 0)
                    throw new InvalidOperationException($"Item de storage ausente no slot {slot}.");
            }
        }

        private static async Task ResetPotionRowsAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            int userId, int characterId)
        {
            await using var command = new MySqlCommand(
                "UPDATE useriteminfo ui JOIN iteminfo def ON def.id=ui.itemid AND def.type=12 " +
                "SET ui.characterid=0,ui.slot=119 WHERE ui.userid=@u " +
                "AND (ui.characterid=0 OR ui.characterid=@char)", connection, transaction);
            command.Parameters.AddWithValue("@u", userId);
            command.Parameters.AddWithValue("@char", characterId);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task EnsureUniqueNonPotionStorageSlotsAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId)
        {
            await using var command = new MySqlCommand(
                "SELECT ui.slot FROM useriteminfo ui " +
                "JOIN iteminfo def ON def.id=ui.itemid AND def.type<>12 " +
                "WHERE ui.userid=@u AND ui.characterid=0 " +
                "GROUP BY ui.slot HAVING COUNT(*)>1 LIMIT 1",
                connection, transaction);
            command.Parameters.AddWithValue("@u", userId);
            if (await command.ExecuteScalarAsync() != null)
                throw new InvalidOperationException(
                    "O layout criaria itens não empilháveis na mesma célula do storage.");
        }

        private static async Task LockInventoryAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId)
        {
            await using var command = new MySqlCommand(
                "SELECT id FROM useriteminfo WHERE userid=@u FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("@u", userId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { }
        }

        private static async Task<StoredItem?> LoadStoredItemAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            int userId, int rowId, int itemId)
        {
            await using var command = new MySqlCommand(
                "SELECT id,slot,level,limittime FROM useriteminfo " +
                "WHERE id=@id AND userid=@u AND characterid=0 AND itemid=@item FOR UPDATE",
                connection, transaction);
            command.Parameters.AddWithValue("@id", rowId);
            command.Parameters.AddWithValue("@u", userId);
            command.Parameters.AddWithValue("@item", itemId);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync()
                ? new StoredItem(reader.GetInt32(0), reader.GetInt32(1),
                    reader.GetInt32(2), reader.GetInt32(3))
                : null;
        }

        private static async Task DeleteInventoryItemAsync(
            MySqlConnection connection, MySqlTransaction transaction, int rowId, int userId)
        {
            await using var command = new MySqlCommand(
                "DELETE FROM useriteminfo WHERE id=@id AND userid=@u", connection, transaction);
            command.Parameters.AddWithValue("@id", rowId);
            command.Parameters.AddWithValue("@u", userId);
            if (await command.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("Item mudou durante a transação.");
        }

        internal static async Task<int> InsertInventoryItemAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId,
            int itemId, int slot, int level = 0, int limitTime = 0, long exp = 0)
        {
            await using var insert = new MySqlCommand(
                "INSERT INTO useriteminfo(userid,characterid,itemid,item_sn,sn_type,level,limittime,slot,exp) " +
                "VALUES(@u,0,@item,0,3,@level,@limit,@slot,@exp)", connection, transaction);
            insert.Parameters.AddWithValue("@u", userId);
            insert.Parameters.AddWithValue("@item", itemId);
            insert.Parameters.AddWithValue("@level", level);
            insert.Parameters.AddWithValue("@limit", limitTime);
            insert.Parameters.AddWithValue("@slot", slot);
            insert.Parameters.AddWithValue("@exp", exp);
            await insert.ExecuteNonQueryAsync();
            int rowId = checked((int)insert.LastInsertedId);
            await using var serial = new MySqlCommand(
                "UPDATE useriteminfo SET item_sn=8000000+id WHERE id=@id", connection, transaction);
            serial.Parameters.AddWithValue("@id", rowId);
            if (await serial.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("Falha ao atribuir serial ao item.");
            return rowId;
        }

        private static int FindNextStorageSlot(int start, IReadOnlySet<int> occupied)
        {
            for (int slot = Math.Max(0, start); slot < 120; slot++)
                if (!occupied.Contains(slot)) return slot;
            throw new InvalidOperationException("Storage sem célula livre.");
        }

        private static async Task<HashSet<int>> LoadStorageSlotsAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId)
        {
            var slots = new HashSet<int>();
            await using var command = new MySqlCommand(
                "SELECT slot FROM useriteminfo WHERE userid=@u AND characterid=0 FOR UPDATE",
                connection, transaction);
            command.Parameters.AddWithValue("@u", userId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) slots.Add(reader.GetInt32(0));
            return slots;
        }

        private sealed record StoredItem(int Id, int Slot, int Level, int LimitTime);
    }
}
