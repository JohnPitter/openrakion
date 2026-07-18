using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;

namespace RakionServer.World.Tests.E2E
{
    internal sealed class PowerUserE2ESandbox : IAsyncDisposable
    {
        public const int CouponItemId = 11999;
        public const int WorkingCash = 100_000;

        private readonly string _connectionString;
        private readonly int? _originalCash;
        private readonly PowerUserBaseline _originalPowerUser;
        private readonly CouponDefinition? _originalCoupon;

        public int GameInfoId { get; }
        public ushort CouponSlot { get; }
        public int CouponRowId { get; private set; }
        public long ItemBaseline { get; }
        public long LedgerBaseline { get; }
        public long CouponLogBaseline { get; }

        private PowerUserE2ESandbox(
            string connectionString, int gameInfoId, ushort couponSlot, int? originalCash,
            PowerUserBaseline originalPowerUser, CouponDefinition? originalCoupon,
            long itemBaseline, long ledgerBaseline, long couponLogBaseline)
        {
            _connectionString = connectionString;
            _originalCash = originalCash;
            _originalPowerUser = originalPowerUser;
            _originalCoupon = originalCoupon;
            GameInfoId = gameInfoId;
            CouponSlot = couponSlot;
            ItemBaseline = itemBaseline;
            LedgerBaseline = ledgerBaseline;
            CouponLogBaseline = couponLogBaseline;
        }

        public static async Task<PowerUserE2ESandbox> CreateAsync(string connectionString)
        {
            await using var connection = await OpenAsync(connectionString);
            int userId = await ScalarAsync<int>(connection,
                "SELECT id FROM usergameinfo WHERE name='test2'");
            int bag = await ScalarAsync<int>(connection,
                "SELECT bag FROM usergameinfo WHERE id=@u", ("@u", userId));
            ushort couponSlot = await FindFreeSlotAsync(
                connection, userId, Math.Clamp(bag * 30, 30, 120));
            object? cash = await ScalarOrNullAsync(connection,
                "SELECT cash FROM cash WHERE id='test2'");
            var sandbox = new PowerUserE2ESandbox(
                connectionString, userId, couponSlot,
                cash == null ? null : Convert.ToInt32(cash),
                await ReadPowerUserBaselineAsync(connection, userId),
                await ReadCouponAsync(connection),
                await MaxAsync(connection, "useriteminfo", "userid", userId),
                await MaxAsync(connection, "logbuypoweruser", "userid", userId),
                await MaxAsync(connection, "logcoupon", "user_id", userId));
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

        public async Task<PowerUserDatabaseState> ReadStateAsync()
        {
            await using var connection = await OpenAsync(_connectionString);
            int cash;
            int points;
            long marker;
            DateTime expires;
            await using (var account = Command(connection,
                "SELECT c.cash,g.powerlevelpoint,g.powertime,g.powertimedate " +
                "FROM usergameinfo g JOIN cash c ON c.id=g.name WHERE g.id=@u",
                ("@u", GameInfoId)))
            await using (var reader = await account.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    throw new InvalidOperationException("Conta Power User E2E ausente.");
                cash = reader.GetInt32(0);
                points = reader.GetInt32(1);
                marker = reader.GetInt64(2);
                expires = reader.GetDateTime(3);
            }
            int couponRows = await ScalarAsync<int>(connection,
                "SELECT COUNT(*) FROM useriteminfo WHERE userid=@u AND id=@id",
                ("@u", GameInfoId), ("@id", CouponRowId));
            return new PowerUserDatabaseState(
                cash, points, marker, expires, couponRows, await ReadLedgersAsync(connection));
        }

        public async Task ExpireAsync()
        {
            await using var connection = await OpenAsync(_connectionString);
            await ExecuteAsync(connection,
                "UPDATE usergameinfo SET powertimedate=DATE_SUB(NOW(),INTERVAL 1 DAY) WHERE id=@u",
                ("@u", GameInfoId));
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = await OpenAsync(_connectionString);
            await using var transaction = await connection.BeginTransactionAsync();
            await ExecuteAsync(connection, transaction,
                "DELETE FROM logbuypoweruser WHERE userid=@u AND id>@baseline",
                ("@u", GameInfoId), ("@baseline", LedgerBaseline));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM logcoupon WHERE user_id=@u AND id>@baseline",
                ("@u", GameInfoId), ("@baseline", CouponLogBaseline));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM useriteminfo WHERE userid=@u AND id>@baseline",
                ("@u", GameInfoId), ("@baseline", ItemBaseline));
            await ExecuteAsync(connection, transaction,
                "UPDATE usergameinfo SET powerlevelpoint=@points,powertime=@marker," +
                "powertimedate=@expires WHERE id=@u",
                ("@points", _originalPowerUser.Points), ("@marker", _originalPowerUser.Marker),
                ("@expires", _originalPowerUser.Expires), ("@u", GameInfoId));
            await RestoreCashAsync(connection, transaction);
            await RestoreCouponAsync(connection, transaction);
            await transaction.CommitAsync();
        }

        private async Task PrepareAsync(MySqlConnection connection)
        {
            await ExecuteAsync(connection,
                "INSERT INTO cash(id,cash) VALUES('test2',@cash) " +
                "ON DUPLICATE KEY UPDATE cash=VALUES(cash)", ("@cash", WorkingCash));
            await ExecuteAsync(connection,
                "UPDATE usergameinfo SET powerlevelpoint=0,powertime=0," +
                "powertimedate=DATE_SUB(NOW(),INTERVAL 1 DAY) WHERE id=@u",
                ("@u", GameInfoId));
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

        private async Task<List<PowerUserLedgerRow>> ReadLedgersAsync(MySqlConnection connection)
        {
            var rows = new List<PowerUserLedgerRow>();
            await using var command = Command(connection,
                "SELECT extend,buycash,powertime_prev,powertime_cur," +
                "powerlevelpoint_prev,powerlevelpoint_cur,COALESCE(coupon_log_id,'') " +
                "FROM logbuypoweruser WHERE userid=@u AND id>@baseline ORDER BY id",
                ("@u", GameInfoId), ("@baseline", LedgerBaseline));
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add(new PowerUserLedgerRow(reader.GetByte(0), reader.GetInt32(1),
                    reader.GetInt64(2), reader.GetInt64(3), reader.GetInt32(4),
                    reader.GetInt32(5), reader.GetString(6)));
            return rows;
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

        private static async Task<PowerUserBaseline> ReadPowerUserBaselineAsync(
            MySqlConnection connection, int userId)
        {
            await using var command = Command(connection,
                "SELECT powerlevelpoint,powertime,CAST(powertimedate AS CHAR) " +
                "FROM usergameinfo WHERE id=@u", ("@u", userId));
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("Perfil test2 ausente.");
            return new PowerUserBaseline(
                reader.GetInt32(0), reader.GetInt64(1), reader.GetString(2));
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
            throw new InvalidOperationException("Conta E2E não possui célula livre para cupom.");
        }

        private static async Task<long> MaxAsync(
            MySqlConnection connection, string table, string ownerColumn, int ownerId) =>
            await ScalarAsync<long>(connection,
                $"SELECT COALESCE(MAX(id),0) FROM `{table}` WHERE `{ownerColumn}`=@u", ("@u", ownerId));

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

        private readonly record struct PowerUserBaseline(int Points, long Marker, string Expires);
        private readonly record struct CouponDefinition(
            int Rate, string Expires, int MinLevel, int MaxLevel, int ForCash);
    }

    internal sealed record PowerUserDatabaseState(
        int Cash, int Points, long Marker, DateTime Expires, int CouponRows,
        IReadOnlyList<PowerUserLedgerRow> Ledgers);
    internal readonly record struct PowerUserLedgerRow(
        byte Mode, int Cost, long PreviousMarker, long CurrentMarker,
        int PreviousPoints, int CurrentPoints, string CouponLogId);
}
