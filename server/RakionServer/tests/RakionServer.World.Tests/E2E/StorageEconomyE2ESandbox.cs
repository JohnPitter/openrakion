using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;

namespace RakionServer.World.Tests.E2E
{
    internal sealed class StorageEconomyE2ESandbox : IAsyncDisposable
    {
        public const int CouponItemId = 11999;
        public const int WorkingCash = 100_000;

        private readonly string _connectionString;
        private readonly int? _originalCash;
        private readonly CouponDefinition? _originalCoupon;

        public int GameInfoId { get; }
        public int Capacity { get; }
        public int CouponRowId { get; private set; }
        public ushort CouponSlot { get; }
        public long ItemRowBaseline { get; }
        public long CashLedgerBaseline { get; }
        public long CouponLogBaseline { get; }
        public long PendingBaseline { get; }

        private StorageEconomyE2ESandbox(
            string connectionString, int gameInfoId, int capacity, ushort couponSlot,
            int? originalCash, CouponDefinition? originalCoupon, long itemRowBaseline,
            long cashLedgerBaseline, long couponLogBaseline, long pendingBaseline)
        {
            _connectionString = connectionString;
            _originalCash = originalCash;
            _originalCoupon = originalCoupon;
            GameInfoId = gameInfoId;
            Capacity = capacity;
            CouponSlot = couponSlot;
            ItemRowBaseline = itemRowBaseline;
            CashLedgerBaseline = cashLedgerBaseline;
            CouponLogBaseline = couponLogBaseline;
            PendingBaseline = pendingBaseline;
        }

        public static async Task<StorageEconomyE2ESandbox> CreateAsync(string connectionString)
        {
            await using var connection = await OpenAsync(connectionString);
            int userId = await ScalarAsync<int>(connection,
                "SELECT id FROM usergameinfo WHERE name='test2'");
            int bag = await ScalarAsync<int>(connection,
                "SELECT bag FROM usergameinfo WHERE id=@u", ("@u", userId));
            int capacity = Math.Clamp(bag * 30, 30, 120);
            ushort couponSlot = await FindFreeSlotAsync(connection, userId, capacity, required: 9);
            object? cashValue = await ScalarOrNullAsync(connection,
                "SELECT cash FROM cash WHERE id='test2'");
            CouponDefinition? coupon = await ReadCouponAsync(connection);
            var sandbox = new StorageEconomyE2ESandbox(
                connectionString, userId, capacity, couponSlot,
                cashValue == null ? null : Convert.ToInt32(cashValue), coupon,
                await MaxAsync(connection, "useriteminfo", "userid", userId),
                await MaxAsync(connection, "logbuycashitem", "userid", userId),
                await MaxAsync(connection, "logcoupon", "user_id", userId),
                await MaxAsync(connection, "pendingpresents", "user_id", userId));
            try
            {
                await sandbox.PrepareAsync(connection);
                return sandbox;
            }
            catch
            {
                await sandbox.DisposeAsync();
                throw;
            }
        }

        private async Task PrepareAsync(MySqlConnection connection)
        {
            await ExecuteAsync(connection,
                "INSERT INTO cash(id,cash) VALUES('test2',@cash) " +
                "ON DUPLICATE KEY UPDATE cash=VALUES(cash)", ("@cash", WorkingCash));
            await ExecuteAsync(connection,
                "INSERT INTO couponinfo(id,discount_rate,expire_days,min_level,max_level,for_cash) " +
                "VALUES(@id,50,DATE_ADD(NOW(),INTERVAL 1 DAY),0,99,1) " +
                "ON DUPLICATE KEY UPDATE discount_rate=50,expire_days=VALUES(expire_days)," +
                "min_level=0,max_level=99,for_cash=1", ("@id", CouponItemId));
            long serial = await ScalarAsync<long>(connection,
                "SELECT GREATEST(8000000,COALESCE(MAX(item_sn),7999999)+1) " +
                "FROM useriteminfo WHERE sn_type=3");
            await using var insert = Command(connection,
                "INSERT INTO useriteminfo(userid,characterid,itemid,level,limittime,slot,item_sn,sn_type) " +
                "VALUES(@u,0,@item,0,0,@slot,@serial,3)",
                ("@u", GameInfoId), ("@item", CouponItemId),
                ("@slot", CouponSlot), ("@serial", serial));
            await insert.ExecuteNonQueryAsync();
            CouponRowId = checked((int)insert.LastInsertedId);
        }

        public async Task<int> ReadCashAsync() => await WithConnectionAsync(connection =>
            ScalarAsync<int>(connection, "SELECT cash FROM cash WHERE id='test2'"));

        public async Task<int> CountNewPendingAsync() => await WithConnectionAsync(connection =>
            ScalarAsync<int>(connection,
                "SELECT COUNT(*) FROM pendingpresents WHERE user_id=@u AND id>@baseline",
                ("@u", GameInfoId), ("@baseline", PendingBaseline)));

        public async Task<int> CountLinkedNewPresentsAsync() => await WithConnectionAsync(connection =>
            ScalarAsync<int>(connection,
                "SELECT COUNT(*) FROM pendingpresents p JOIN logpresent l ON l.pending_id=p.id " +
                "WHERE p.user_id=@u AND p.id>@baseline AND l.user_id=@u",
                ("@u", GameInfoId), ("@baseline", PendingBaseline)));

        public async Task<List<PurchasedRow>> ReadPurchasedRowsAsync() =>
            await WithConnectionAsync(async connection =>
            {
                var rows = new List<PurchasedRow>();
                await using var command = Command(connection,
                    "SELECT id,itemid,slot,item_sn,sn_type FROM useriteminfo " +
                    "WHERE userid=@u AND id>@baseline ORDER BY id",
                    ("@u", GameInfoId), ("@baseline", ItemRowBaseline));
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    rows.Add(new PurchasedRow(reader.GetInt32(0), reader.GetInt32(1),
                        reader.GetInt32(2), reader.GetInt64(3), reader.GetByte(4)));
                return rows;
            });

        public async Task<List<CashLedgerRow>> ReadCashLedgersAsync() =>
            await WithConnectionAsync(async connection =>
            {
                var rows = new List<CashLedgerRow>();
                await using var command = Command(connection,
                    "SELECT id,itemid,price,cash_prev,cash_cur,COALESCE(coupon_log_id,'') " +
                    "FROM logbuycashitem WHERE userid=@u AND id>@baseline ORDER BY id",
                    ("@u", GameInfoId), ("@baseline", CashLedgerBaseline));
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    rows.Add(new CashLedgerRow(reader.GetInt64(0), reader.GetInt32(1),
                        reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
                        reader.GetString(5)));
                return rows;
            });

        public async Task<CouponLogRow> ReadCouponLogAsync() =>
            await WithConnectionAsync(async connection =>
            {
                await using var command = Command(connection,
                    "SELECT id,coupon_id,item_id,discount_amount FROM logcoupon " +
                    "WHERE user_id=@u AND id>@baseline ORDER BY id",
                    ("@u", GameInfoId), ("@baseline", CouponLogBaseline));
                await using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    throw new InvalidOperationException("Compra não criou logcoupon.");
                var row = new CouponLogRow(reader.GetInt64(0), reader.GetInt32(1),
                    reader.GetInt32(2), reader.GetInt32(3));
                if (await reader.ReadAsync())
                    throw new InvalidOperationException("Compra criou mais de um logcoupon.");
                return row;
            });

        public async ValueTask DisposeAsync()
        {
            await using var connection = await OpenAsync(_connectionString);
            await using var transaction = await connection.BeginTransactionAsync();
            await ExecuteAsync(connection, transaction,
                "DELETE FROM logpresent WHERE user_id=@u AND pending_id>@baseline",
                ("@u", GameInfoId), ("@baseline", PendingBaseline));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM pendingpresents WHERE user_id=@u AND id>@baseline",
                ("@u", GameInfoId), ("@baseline", PendingBaseline));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM useriteminfo WHERE userid=@u AND id>@baseline",
                ("@u", GameInfoId), ("@baseline", ItemRowBaseline));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM logbuycashitem WHERE userid=@u AND id>@baseline",
                ("@u", GameInfoId), ("@baseline", CashLedgerBaseline));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM logcoupon WHERE user_id=@u AND id>@baseline",
                ("@u", GameInfoId), ("@baseline", CouponLogBaseline));
            await RestoreCashAsync(connection, transaction);
            await RestoreCouponAsync(connection, transaction);
            await transaction.CommitAsync();
        }

        private async Task RestoreCashAsync(
            MySqlConnection connection, MySqlTransaction transaction)
        {
            if (_originalCash.HasValue)
                await ExecuteAsync(connection, transaction,
                    "UPDATE cash SET cash=@cash WHERE id='test2'", ("@cash", _originalCash.Value));
            else
                await ExecuteAsync(connection, transaction, "DELETE FROM cash WHERE id='test2'");
        }

        private async Task RestoreCouponAsync(
            MySqlConnection connection, MySqlTransaction transaction)
        {
            if (_originalCoupon == null)
            {
                await ExecuteAsync(connection, transaction,
                    "DELETE FROM couponinfo WHERE id=@id", ("@id", CouponItemId));
                return;
            }
            CouponDefinition value = _originalCoupon.Value;
            await ExecuteAsync(connection, transaction,
                "UPDATE couponinfo SET discount_rate=@rate,expire_days=@expire," +
                "min_level=@min,max_level=@max,for_cash=@cash WHERE id=@id",
                ("@rate", value.Rate), ("@expire", value.Expires), ("@min", value.MinLevel),
                ("@max", value.MaxLevel), ("@cash", value.ForCash), ("@id", CouponItemId));
        }

        private static async Task<CouponDefinition?> ReadCouponAsync(MySqlConnection connection)
        {
            await using var command = Command(connection,
                "SELECT discount_rate,CAST(expire_days AS CHAR),min_level,max_level,for_cash " +
                "FROM couponinfo WHERE id=@id", ("@id", CouponItemId));
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync()
                ? new CouponDefinition(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2),
                    reader.GetInt32(3), reader.GetInt32(4))
                : null;
        }

        private static async Task<ushort> FindFreeSlotAsync(
            MySqlConnection connection, int userId, int capacity, int required)
        {
            var occupied = new bool[capacity];
            await using var command = Command(connection,
                "SELECT slot FROM useriteminfo WHERE userid=@u AND characterid=0",
                ("@u", userId));
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int slot = reader.GetInt32(0);
                if (slot >= 0 && slot < capacity) occupied[slot] = true;
            }
            int free = 0;
            int first = -1;
            for (int i = 0; i < capacity; i++)
                if (!occupied[i]) { if (first < 0) first = i; free++; }
            if (free < required) throw new InvalidOperationException(
                $"Conta E2E precisa de {required} células livres; encontrou {free}.");
            return checked((ushort)first);
        }

        private static async Task<long> MaxAsync(
            MySqlConnection connection, string table, string ownerColumn, int ownerId) =>
            await ScalarAsync<long>(connection,
                $"SELECT COALESCE(MAX(id),0) FROM `{table}` WHERE `{ownerColumn}`=@u", ("@u", ownerId));

        private async Task<T> WithConnectionAsync<T>(Func<MySqlConnection, Task<T>> action)
        {
            await using var connection = await OpenAsync(_connectionString);
            return await action(connection);
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
            object? value = await ScalarOrNullAsync(connection, sql, values);
            return (T)Convert.ChangeType(value!, typeof(T));
        }

        private static async Task<object?> ScalarOrNullAsync(
            MySqlConnection connection, string sql, params (string Name, object Value)[] values)
        {
            await using var command = Command(connection, sql, values);
            return await command.ExecuteScalarAsync();
        }

        private static async Task ExecuteAsync(
            MySqlConnection connection, string sql, params (string Name, object Value)[] values)
        {
            await using var command = Command(connection, sql, values);
            await command.ExecuteNonQueryAsync();
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

        private readonly record struct CouponDefinition(
            int Rate, string Expires, int MinLevel, int MaxLevel, int ForCash);
    }

    internal readonly record struct PurchasedRow(
        int Id, int ItemId, int Slot, long Serial, byte SerialType);
    internal readonly record struct CashLedgerRow(
        long Id, int ItemId, int Price, int PreviousCash, int CurrentCash, string CouponLogId);
    internal readonly record struct CouponLogRow(
        long Id, int CouponItemId, int OperationItemId, int Discount);
}
