using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;

namespace RakionServer.World.Tests.E2E
{
    internal sealed class EnchantE2ESandbox : IAsyncDisposable
    {
        private readonly string _connectionString;

        public int GameInfoId { get; }
        public long ItemRowBaseline { get; }
        public long LedgerBaseline { get; }
        public EnchantFixtureItem Target { get; private set; }
        public EnchantFixtureItem Catalyst { get; private set; }
        public EnchantFixtureItem Material { get; private set; }

        private EnchantE2ESandbox(
            string connectionString, int gameInfoId, long itemRowBaseline, long ledgerBaseline)
        {
            _connectionString = connectionString;
            GameInfoId = gameInfoId;
            ItemRowBaseline = itemRowBaseline;
            LedgerBaseline = ledgerBaseline;
        }

        public static async Task<EnchantE2ESandbox> CreateAsync(string connectionString)
        {
            await using var connection = await OpenAsync(connectionString);
            int userId = await ScalarAsync<int>(connection,
                "SELECT id FROM usergameinfo WHERE name='test2'");
            int bag = await ScalarAsync<int>(connection,
                "SELECT bag FROM usergameinfo WHERE id=@u", ("@u", userId));
            int capacity = Math.Clamp(bag * 30, 30, 120);
            byte[] slots = await FindFreeSlotsAsync(connection, userId, capacity, 3);
            var sandbox = new EnchantE2ESandbox(connectionString, userId,
                await MaxAsync(connection, "useriteminfo", userId),
                await MaxAsync(connection, "logenchant", userId));
            try
            {
                long serial = await ScalarAsync<long>(connection,
                    "SELECT GREATEST(8000000,COALESCE(MAX(item_sn),7999999)+1) " +
                    "FROM useriteminfo WHERE sn_type=3");
                sandbox.Target = await InsertAsync(
                    connection, userId, 1001, level: 4, slots[0], serial);
                sandbox.Catalyst = await InsertAsync(
                    connection, userId, 13001, level: 0, slots[1], serial + 1);
                sandbox.Material = await InsertAsync(
                    connection, userId, 14001, level: 0, slots[2], serial + 2);
                return sandbox;
            }
            catch
            {
                await sandbox.DisposeAsync();
                throw;
            }
        }

        public async Task<EnchantLedgerRow> ReadLedgerAsync()
        {
            await using var connection = await OpenAsync(_connectionString);
            await using var command = Command(connection,
                "SELECT id,operation_id,target_row_id,target_item_id,level_prev,level_cur," +
                "catalyst_row_id,material_row_ids,result,chance,config_version " +
                "FROM logenchant WHERE userid=@u AND id>@baseline ORDER BY id",
                ("@u", GameInfoId), ("@baseline", LedgerBaseline));
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("Enchant não criou ledger.");
            var row = new EnchantLedgerRow(
                reader.GetInt64(0), Convert.ToString(reader.GetValue(1))!,
                reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetString(7),
                reader.GetByte(8), reader.GetDouble(9), reader.GetInt32(10));
            if (await reader.ReadAsync())
                throw new InvalidOperationException("Replay criou mais de um ledger de enchant.");
            return row;
        }

        public async Task<EnchantDatabaseState> ReadStateAsync()
        {
            await using var connection = await OpenAsync(_connectionString);
            int targetLevel = await ScalarAsync<int>(connection,
                "SELECT level FROM useriteminfo WHERE id=@id AND userid=@u",
                ("@id", Target.RowId), ("@u", GameInfoId));
            int inputs = await ScalarAsync<int>(connection,
                "SELECT COUNT(*) FROM useriteminfo WHERE userid=@u AND id IN (@cat,@mat)",
                ("@u", GameInfoId), ("@cat", Catalyst.RowId), ("@mat", Material.RowId));
            return new EnchantDatabaseState(targetLevel, inputs);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = await OpenAsync(_connectionString);
            await using var transaction = await connection.BeginTransactionAsync();
            await ExecuteAsync(connection, transaction,
                "DELETE FROM logenchant WHERE userid=@u AND id>@baseline",
                ("@u", GameInfoId), ("@baseline", LedgerBaseline));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM useriteminfo WHERE userid=@u AND id>@baseline",
                ("@u", GameInfoId), ("@baseline", ItemRowBaseline));
            await transaction.CommitAsync();
        }

        private static async Task<EnchantFixtureItem> InsertAsync(
            MySqlConnection connection, int userId, int itemId, int level, byte slot, long serial)
        {
            await using var command = Command(connection,
                "INSERT INTO useriteminfo(userid,characterid,itemid,item_sn,sn_type,level," +
                "limittime,slot,exp) VALUES(@u,0,@item,@serial,3,@level,0,@slot,0)",
                ("@u", userId), ("@item", itemId), ("@serial", serial),
                ("@level", level), ("@slot", slot));
            await command.ExecuteNonQueryAsync();
            return new EnchantFixtureItem(
                checked((int)command.LastInsertedId), itemId, level, slot, checked((uint)serial));
        }

        private static async Task<byte[]> FindFreeSlotsAsync(
            MySqlConnection connection, int userId, int capacity, int required)
        {
            var occupied = new bool[capacity];
            await using var command = Command(connection,
                "SELECT slot FROM useriteminfo WHERE userid=@u AND characterid=0", ("@u", userId));
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int slot = reader.GetInt32(0);
                if (slot >= 0 && slot < capacity) occupied[slot] = true;
            }
            var slots = new List<byte>(required);
            for (int i = 0; i < occupied.Length && slots.Count < required; i++)
                if (!occupied[i]) slots.Add(checked((byte)i));
            if (slots.Count != required)
                throw new InvalidOperationException(
                    $"Conta E2E precisa de {required} células livres; encontrou {slots.Count}.");
            return slots.ToArray();
        }

        private static async Task<long> MaxAsync(
            MySqlConnection connection, string table, int userId) =>
            await ScalarAsync<long>(connection,
                $"SELECT COALESCE(MAX(id),0) FROM `{table}` WHERE userid=@u", ("@u", userId));

        private static async Task<MySqlConnection> OpenAsync(string connectionString)
        {
            var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            return connection;
        }

        private static async Task<T> ScalarAsync<T>(
            MySqlConnection connection, string sql, params (string Name, object Value)[] values)
        {
            await using var command = Command(connection, sql, values);
            object? value = await command.ExecuteScalarAsync();
            return (T)Convert.ChangeType(value!, typeof(T));
        }

        private static async Task ExecuteAsync(
            MySqlConnection connection, MySqlTransaction transaction, string sql,
            params (string Name, object Value)[] values)
        {
            await using var command = Command(connection, sql, values);
            command.Transaction = transaction;
            await command.ExecuteNonQueryAsync();
        }

        private static MySqlCommand Command(
            MySqlConnection connection, string sql,
            params (string Name, object Value)[] values)
        {
            var command = new MySqlCommand(sql, connection);
            foreach ((string name, object value) in values)
                command.Parameters.AddWithValue(name, value);
            return command;
        }
    }

    internal readonly record struct EnchantFixtureItem(
        int RowId, int ItemId, int Level, byte Slot, uint Serial);
    internal readonly record struct EnchantDatabaseState(int TargetLevel, int InputCount);
    internal readonly record struct EnchantLedgerRow(
        long Id, string OperationId, int TargetRowId, int TargetItemId,
        int PreviousLevel, int CurrentLevel, int CatalystRowId, string MaterialRowIds,
        byte Result, double Chance, int ConfigVersion);
}
