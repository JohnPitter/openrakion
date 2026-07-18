using System;
using System.Threading.Tasks;
using MySqlConnector;

namespace RakionServer.World.Tests.E2E
{
    internal sealed class PresentE2ESandbox : IAsyncDisposable
    {
        public const int FirstItemId = 1040;
        public const int SecondItemId = 1240;

        private readonly string _connectionString;

        public int GameInfoId { get; }
        public long ItemRowBaseline { get; }
        public ushort AcceptSlot { get; }
        public int FirstPendingId { get; private set; }
        public int SecondPendingId { get; private set; }

        private PresentE2ESandbox(
            string connectionString, int gameInfoId, long itemRowBaseline, ushort acceptSlot)
        {
            _connectionString = connectionString;
            GameInfoId = gameInfoId;
            ItemRowBaseline = itemRowBaseline;
            AcceptSlot = acceptSlot;
        }

        public static async Task<PresentE2ESandbox> CreateAsync(string connectionString)
        {
            await using var connection = await OpenAsync(connectionString);
            int userId = await ScalarAsync<int>(connection,
                "SELECT id FROM usergameinfo WHERE name='test2'");
            int existingPresents = await ScalarAsync<int>(connection,
                "SELECT COUNT(*) FROM pendingpresents WHERE user_id=@u", ("@u", userId));
            if (existingPresents != 0)
                throw new InvalidOperationException(
                    "A conta E2E test2 precisa iniciar sem presentes pendentes.");

            int bag = await ScalarAsync<int>(connection,
                "SELECT bag FROM usergameinfo WHERE id=@u", ("@u", userId));
            ushort freeSlot = await FindFreeSlotAsync(
                connection, userId, Math.Clamp(bag * 30, 30, 120));
            long itemBaseline = await ScalarAsync<long>(connection,
                "SELECT COALESCE(MAX(id),0) FROM useriteminfo WHERE userid=@u", ("@u", userId));
            var sandbox = new PresentE2ESandbox(
                connectionString, userId, itemBaseline, freeSlot);
            try
            {
                sandbox.FirstPendingId = await InsertPresentAsync(
                    connection, userId, FirstItemId);
                sandbox.SecondPendingId = await InsertPresentAsync(
                    connection, userId, SecondItemId);
                return sandbox;
            }
            catch
            {
                await sandbox.DisposeAsync();
                throw;
            }
        }

        public async Task<PresentDatabaseState> ReadStateAsync()
        {
            await using var connection = await OpenAsync(_connectionString);
            int pending = await ScalarAsync<int>(connection,
                "SELECT COUNT(*) FROM pendingpresents WHERE user_id=@u " +
                "AND id IN (@first,@second)",
                ("@u", GameInfoId), ("@first", FirstPendingId), ("@second", SecondPendingId));
            AcceptedPresentRow accepted = await ReadAcceptedItemAsync(connection);
            PresentAuditState first = await ReadAuditAsync(connection, FirstPendingId);
            PresentAuditState second = await ReadAuditAsync(connection, SecondPendingId);
            return new PresentDatabaseState(pending, accepted, first, second);
        }

        public async Task<int> CountPendingAsync()
        {
            await using var connection = await OpenAsync(_connectionString);
            return await ScalarAsync<int>(connection,
                "SELECT COUNT(*) FROM pendingpresents WHERE user_id=@u " +
                "AND id IN (@first,@second)",
                ("@u", GameInfoId), ("@first", FirstPendingId), ("@second", SecondPendingId));
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = await OpenAsync(_connectionString);
            await using var transaction = await connection.BeginTransactionAsync();
            await ExecuteAsync(connection, transaction,
                "DELETE FROM logpresent WHERE user_id=@u AND pending_id IN (@first,@second)",
                ("@u", GameInfoId), ("@first", FirstPendingId), ("@second", SecondPendingId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM pendingpresents WHERE user_id=@u AND id IN (@first,@second)",
                ("@u", GameInfoId), ("@first", FirstPendingId), ("@second", SecondPendingId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM useriteminfo WHERE userid=@u AND id>@baseline",
                ("@u", GameInfoId), ("@baseline", ItemRowBaseline));
            await transaction.CommitAsync();
        }

        private async Task<AcceptedPresentRow> ReadAcceptedItemAsync(MySqlConnection connection)
        {
            await using var command = Command(connection,
                "SELECT id,itemid,slot,item_sn,sn_type FROM useriteminfo " +
                "WHERE userid=@u AND id>@baseline ORDER BY id",
                ("@u", GameInfoId), ("@baseline", ItemRowBaseline));
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("Aceite não criou item no storage.");
            var row = new AcceptedPresentRow(reader.GetInt32(0), reader.GetInt32(1),
                reader.GetUInt16(2), reader.GetInt64(3), reader.GetByte(4));
            if (await reader.ReadAsync())
                throw new InvalidOperationException("Jornada criou mais de um item no storage.");
            return row;
        }

        private static async Task<PresentAuditState> ReadAuditAsync(
            MySqlConnection connection, int pendingId)
        {
            await using var command = Command(connection,
                "SELECT COALESCE(accept_time>='2000-01-01',0)," +
                "COALESCE(dispose_time>='2000-01-01',0) " +
                "FROM logpresent WHERE pending_id=@id", ("@id", pendingId));
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException($"Auditoria do presente {pendingId} ausente.");
            return new PresentAuditState(reader.GetBoolean(0), reader.GetBoolean(1));
        }

        private static async Task<int> InsertPresentAsync(
            MySqlConnection connection, int userId, int itemId)
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await using var pending = Command(connection,
                "INSERT INTO pendingpresents(present_id,user_id,added_time) " +
                "VALUES(@item,@u,NOW())", ("@item", itemId), ("@u", userId));
            pending.Transaction = transaction;
            await pending.ExecuteNonQueryAsync();
            int pendingId = checked((int)pending.LastInsertedId);
            await ExecuteAsync(connection, transaction,
                "INSERT INTO logpresent(pending_id,present_id,sender_id,user_id,present_time) " +
                "VALUES(@pending,@item,0,@u,NOW())",
                ("@pending", pendingId), ("@item", itemId), ("@u", userId));
            await transaction.CommitAsync();
            return pendingId;
        }

        private static async Task<ushort> FindFreeSlotAsync(
            MySqlConnection connection, int userId, int capacity)
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
            for (int slot = 0; slot < occupied.Length; slot++)
                if (!occupied[slot]) return checked((ushort)slot);
            throw new InvalidOperationException("Conta E2E não possui célula livre no storage.");
        }

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

    internal readonly record struct AcceptedPresentRow(
        int RowId, int ItemId, ushort Slot, long Serial, byte SerialType);
    internal readonly record struct PresentAuditState(bool Accepted, bool Disposed);
    internal readonly record struct PresentDatabaseState(
        int PendingCount, AcceptedPresentRow Accepted,
        PresentAuditState FirstAudit, PresentAuditState SecondAudit);
}
