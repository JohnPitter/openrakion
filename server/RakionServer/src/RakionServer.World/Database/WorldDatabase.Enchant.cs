using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Database
{
    public sealed partial class WorldDatabase
    {
        private static async Task EnsureEnchantLedgerSchemaAsync(MySqlConnection connection)
        {
            await Exec(connection,
                "ALTER TABLE enchant_config ADD COLUMN IF NOT EXISTS " +
                "config_version INT NOT NULL DEFAULT 1");
            await Exec(connection,
                "ALTER TABLE enchant_config ADD COLUMN IF NOT EXISTS " +
                "original_outcomes TINYINT(1) NOT NULL DEFAULT 1");
            await Exec(connection,
                "UPDATE enchant_catalyzer SET base_success=CASE catalyzer_id " +
                "WHEN 13001 THEN 0.700 WHEN 13002 THEN 0.850 WHEN 13003 THEN 0.900 " +
                "WHEN 13004 THEN 0.750 WHEN 13005 THEN 0.950 ELSE base_success END," +
                "decay=CASE catalyzer_id WHEN 13001 THEN 0.060 WHEN 13002 THEN 0.060 " +
                "WHEN 13003 THEN 0.050 WHEN 13004 THEN 0.040 WHEN 13005 THEN 0.030 " +
                "ELSE decay END WHERE catalyzer_id BETWEEN 13001 AND 13005 AND " +
                "(SELECT config_version FROM enchant_config WHERE id=1)<2");
            await Exec(connection,
                "UPDATE enchant_config SET original_outcomes=1,config_version=2 " +
                "WHERE id=1 AND config_version<2");
            await Exec(connection,
                "CREATE TABLE IF NOT EXISTS logenchant (" +
                "id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY," +
                "operation_id CHAR(36) NOT NULL UNIQUE," +
                "userid INT NOT NULL,target_row_id INT NOT NULL,target_item_id INT NOT NULL," +
                "level_prev TINYINT NOT NULL,level_cur TINYINT NOT NULL," +
                "catalyst_row_id INT NOT NULL,material_row_ids VARCHAR(64) NOT NULL," +
                "result TINYINT NOT NULL,chance DECIMAL(8,7) NOT NULL," +
                "config_version INT NOT NULL,createtime DATETIME NOT NULL) ENGINE=InnoDB");
        }

        public async Task<uint[]?> LoadEnchantSerialsAsync(EnchantSelection selection)
        {
            EnchantItemRef[] items = SelectionItems(selection);
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                Dictionary<int, LockedEnchantItem> rows = await LoadEnchantRowsAsync(
                    connection, null, selection.UserId, items, false);
                if (!SelectionMatches(selection, rows)) return null;
                return items.Select(item => checked((uint)rows[item.RowId].Serial)).ToArray();
            }
            catch (Exception ex)
            {
                Log.Error("enchant", "preview user={0}: {1}", selection.UserId, ex.Message);
                return null;
            }
        }

        public async Task<EnchantCommitResult> CommitEnchantAsync(PendingEnchant pending)
        {
            EnchantSelection selection = pending.Selection;
            EnchantItemRef[] items = SelectionItems(selection);
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                Dictionary<int, LockedEnchantItem> rows = await LoadEnchantRowsAsync(
                    connection, transaction, selection.UserId, items, true);
                if (!SelectionMatches(selection, rows))
                    return new EnchantCommitResult(false);

                int updated = await UpdateEnchantTargetAsync(
                    connection, transaction, selection, pending.NewLevel);
                int removed = await DeleteEnchantInputsAsync(
                    connection, transaction, selection);
                if (updated != 1 || removed != items.Length - 1)
                    return new EnchantCommitResult(false);
                await InsertEnchantLedgerAsync(connection, transaction, pending);
                await transaction.CommitAsync();
                return new EnchantCommitResult(true, pending.NewLevel);
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                return await LoadCommittedEnchantAsync(pending.OperationId);
            }
            catch (Exception ex)
            {
                Log.Error("enchant", "commit user={0} op={1}: {2}",
                    selection.UserId, pending.OperationId, ex.Message);
                return new EnchantCommitResult(false);
            }
        }

        private sealed record LockedEnchantItem(
            int RowId, int ItemId, int Serial, int Level, int Slot, int CharacterId);

        private static EnchantItemRef[] SelectionItems(EnchantSelection selection) =>
            [selection.Target, selection.Catalyst, .. selection.Materials];

        private static async Task<Dictionary<int, LockedEnchantItem>> LoadEnchantRowsAsync(
            MySqlConnection connection, MySqlTransaction? transaction, int userId,
            IReadOnlyList<EnchantItemRef> items, bool forUpdate)
        {
            string parameters = string.Join(",", items.Select((_, i) => "@id" + i));
            string sql = "SELECT id,itemid,item_sn,level,slot,characterid FROM useriteminfo " +
                $"WHERE userid=@u AND id IN ({parameters}) AND " +
                "(limittime=0 OR limittime>=((TO_DAYS(NOW())*24+HOUR(NOW()))*60+MINUTE(NOW())))" +
                (forUpdate ? " FOR UPDATE" : "");
            await using var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@u", userId);
            for (int i = 0; i < items.Count; i++)
                command.Parameters.AddWithValue("@id" + i, items[i].RowId);
            var rows = new Dictionary<int, LockedEnchantItem>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var item = new LockedEnchantItem(
                    reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                    reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5));
                rows[item.RowId] = item;
            }
            return rows;
        }

        private static bool SelectionMatches(
            EnchantSelection selection, IReadOnlyDictionary<int, LockedEnchantItem> rows)
        {
            EnchantItemRef[] expected = SelectionItems(selection);
            if (rows.Count != expected.Length) return false;
            foreach (EnchantItemRef item in expected)
            {
                if (!rows.TryGetValue(item.RowId, out LockedEnchantItem? row) ||
                    row.ItemId != item.ItemId || row.Slot != item.Slot || row.CharacterId != 0)
                    return false;
            }
            return rows[selection.Target.RowId].Level == selection.TargetLevel;
        }

        private static async Task<int> UpdateEnchantTargetAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            EnchantSelection selection, int newLevel)
        {
            await using var command = new MySqlCommand(
                "UPDATE useriteminfo SET level=@new WHERE id=@id AND userid=@u " +
                "AND characterid=0 AND level=@old", connection, transaction);
            command.Parameters.AddWithValue("@new", newLevel);
            command.Parameters.AddWithValue("@old", selection.TargetLevel);
            command.Parameters.AddWithValue("@id", selection.Target.RowId);
            command.Parameters.AddWithValue("@u", selection.UserId);
            return await command.ExecuteNonQueryAsync();
        }

        private static async Task<int> DeleteEnchantInputsAsync(
            MySqlConnection connection, MySqlTransaction transaction, EnchantSelection selection)
        {
            EnchantItemRef[] inputs = [selection.Catalyst, .. selection.Materials];
            string parameters = string.Join(",", inputs.Select((_, i) => "@input" + i));
            await using var command = new MySqlCommand(
                $"DELETE FROM useriteminfo WHERE userid=@u AND characterid=0 AND id IN ({parameters})",
                connection, transaction);
            command.Parameters.AddWithValue("@u", selection.UserId);
            for (int i = 0; i < inputs.Length; i++)
                command.Parameters.AddWithValue("@input" + i, inputs[i].RowId);
            return await command.ExecuteNonQueryAsync();
        }

        private static async Task InsertEnchantLedgerAsync(
            MySqlConnection connection, MySqlTransaction transaction, PendingEnchant pending)
        {
            EnchantSelection selection = pending.Selection;
            string materials = string.Join(",", selection.Materials.Select(item => item.RowId));
            await using var command = new MySqlCommand(
                "INSERT INTO logenchant(operation_id,userid,target_row_id,target_item_id," +
                "level_prev,level_cur,catalyst_row_id,material_row_ids,result,chance," +
                "config_version,createtime) VALUES(@op,@u,@target,@item,@prev,@cur,@cat," +
                "@materials,@result,@chance,@version,NOW())", connection, transaction);
            command.Parameters.AddWithValue("@op", pending.OperationId);
            command.Parameters.AddWithValue("@u", selection.UserId);
            command.Parameters.AddWithValue("@target", selection.Target.RowId);
            command.Parameters.AddWithValue("@item", selection.Target.ItemId);
            command.Parameters.AddWithValue("@prev", selection.TargetLevel);
            command.Parameters.AddWithValue("@cur", pending.NewLevel);
            command.Parameters.AddWithValue("@cat", selection.Catalyst.RowId);
            command.Parameters.AddWithValue("@materials", materials);
            command.Parameters.AddWithValue("@result", pending.Result);
            command.Parameters.AddWithValue("@chance", pending.Chance);
            command.Parameters.AddWithValue("@version", pending.ConfigVersion);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<EnchantCommitResult> LoadCommittedEnchantAsync(string operationId)
        {
            await using var connection = new MySqlConnection(_conn);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "SELECT level_cur FROM logenchant WHERE operation_id=@op", connection);
            command.Parameters.AddWithValue("@op", operationId);
            object? level = await command.ExecuteScalarAsync();
            return level == null
                ? new EnchantCommitResult(false)
                : new EnchantCommitResult(true, Convert.ToInt32(level));
        }
    }
}
