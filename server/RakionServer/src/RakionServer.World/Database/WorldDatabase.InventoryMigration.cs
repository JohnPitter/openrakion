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
        private static async Task MigrateLegacyItemBoxAsync(MySqlConnection connection)
        {
            var legacy = new List<LegacyBoxItem>();
            await using (var read = new MySqlCommand(
                "SELECT b.id,b.userid,b.itemid,IFNULL(b.limittime,0),b.level,b.boxslot,b.qslot," +
                "IFNULL(def.type,-1),(SELECT id FROM characterinfo ch " +
                "WHERE ch.userid=b.userid AND ch.used=1 ORDER BY ch.id LIMIT 1) " +
                "FROM itembox b LEFT JOIN iteminfo def ON def.id=b.itemid ORDER BY b.userid,b.id",
                connection))
            await using (var reader = await read.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    legacy.Add(new LegacyBoxItem(
                        reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                        checked((int)reader.GetInt64(3)), reader.GetInt32(4),
                        reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.GetInt32(6),
                        reader.GetInt32(7), reader.IsDBNull(8) ? 0 : reader.GetInt32(8)));
            }
            if (legacy.Count == 0) return;

            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            var storage = await LoadOccupiedStorageAsync(connection, transaction);
            foreach (LegacyBoxItem item in legacy)
            {
                bool activePotion = item.QSlot is >= 1 and <= 19 && item.ActiveCharacterId > 0;
                int characterId = activePotion ? item.ActiveCharacterId : 0;
                int slot = activePotion
                    ? item.QSlot - 1
                    : ResolveMigrationSlot(item, storage);
                int rowId = await InsertInventoryItemAsync(connection, transaction,
                    item.UserId, item.ItemId, slot, item.Level, item.LimitTime);
                if (characterId > 0)
                {
                    await using var equip = new MySqlCommand(
                        "UPDATE useriteminfo SET characterid=@char WHERE id=@id",
                        connection, transaction);
                    equip.Parameters.AddWithValue("@char", characterId);
                    equip.Parameters.AddWithValue("@id", rowId);
                    await equip.ExecuteNonQueryAsync();
                }
                await using var delete = new MySqlCommand(
                    "DELETE FROM itembox WHERE id=@id", connection, transaction);
                delete.Parameters.AddWithValue("@id", item.Id);
                if (await delete.ExecuteNonQueryAsync() != 1)
                    throw new InvalidOperationException($"Itembox {item.Id} mudou durante a migração.");
            }
            await transaction.CommitAsync();
            Log.Ok("db", "migração canônica: {0} linha(s) itembox -> useriteminfo", legacy.Count);
        }

        private static async Task<Dictionary<int, Dictionary<int, int>>> LoadOccupiedStorageAsync(
            MySqlConnection connection, MySqlTransaction transaction)
        {
            var occupied = new Dictionary<int, Dictionary<int, int>>();
            await using var command = new MySqlCommand(
                "SELECT userid,slot,itemid FROM useriteminfo WHERE characterid=0 FOR UPDATE",
                connection, transaction);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int userId = reader.GetInt32(0);
                if (!occupied.TryGetValue(userId, out var cells))
                {
                    cells = new Dictionary<int, int>();
                    occupied[userId] = cells;
                }
                cells.TryAdd(reader.GetInt32(1), reader.GetInt32(2));
            }
            return occupied;
        }

        private static int ResolveMigrationSlot(
            LegacyBoxItem item, Dictionary<int, Dictionary<int, int>> storage)
        {
            if (!storage.TryGetValue(item.UserId, out var cells))
            {
                cells = new Dictionary<int, int>();
                storage[item.UserId] = cells;
            }
            int desired = item.BoxSlot is >= 0 and < 120 ? item.BoxSlot.Value : -1;
            if (desired >= 0 && (!cells.TryGetValue(desired, out int present) ||
                                 (item.Type == 12 && present == item.ItemId)))
            {
                cells[desired] = item.ItemId;
                return desired;
            }
            for (int slot = 0; slot < 120; slot++)
            {
                if (cells.ContainsKey(slot)) continue;
                cells[slot] = item.ItemId;
                return slot;
            }
            throw new InvalidOperationException($"Storage do usuário {item.UserId} excede 120 células.");
        }

        private static async Task NormalizeItemSerialsAsync(MySqlConnection connection)
        {
            await using (var defaults = new MySqlCommand(
                "UPDATE useriteminfo SET item_sn=8000000+id,sn_type=3 " +
                "WHERE item_sn<=0 OR item_sn=8000",
                connection))
                await defaults.ExecuteNonQueryAsync();

            var duplicateIds = new List<int>();
            await using (var command = new MySqlCommand(
                "SELECT u.id FROM useriteminfo u JOIN " +
                "(SELECT sn_type,item_sn,MIN(id) keep_id FROM useriteminfo " +
                "GROUP BY sn_type,item_sn HAVING COUNT(*)>1) d " +
                "ON d.sn_type=u.sn_type AND d.item_sn=u.item_sn WHERE u.id<>d.keep_id", connection))
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) duplicateIds.Add(reader.GetInt32(0));

            foreach (int id in duplicateIds)
            {
                await using var update = new MySqlCommand(
                    "UPDATE useriteminfo SET item_sn=8000000+id,sn_type=3 WHERE id=@id", connection);
                update.Parameters.AddWithValue("@id", id);
                await update.ExecuteNonQueryAsync();
            }
        }

        private sealed record LegacyBoxItem(
            int Id, int UserId, int ItemId, int LimitTime, int Level,
            int? BoxSlot, int QSlot, int Type, int ActiveCharacterId);
    }
}
