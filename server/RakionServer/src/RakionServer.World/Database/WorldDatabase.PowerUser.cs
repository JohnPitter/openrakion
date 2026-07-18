using System;
using System.Data;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Database
{
    public enum PowerUserPurchaseStatus : byte
    {
        Success = 0,
        Failed = 1,
        InProgress = 2,
        InsufficientCash = 3,
        InvalidCouponItem = 0x14,
        CouponNotForCash = 0x15,
        CouponDefinitionMissing = 0x16
    }

    public sealed record PowerUserPurchaseCommand(
        int UserId, string AccountId, byte Mode, int BasePrice, int BonusPoints,
        int DurationDays, CharacterPayment Payment);

    public sealed record PowerUserPurchaseResult(
        PowerUserPurchaseStatus Status, int Gold = 0, int Cash = 0,
        uint PowerTimeMarker = 0, int PowerLevelPoints = 0,
        DateTime? ExpiresAt = null, int CouponRowId = 0, int CouponItemId = 0,
        int[]? Presents = null);

    public sealed record StatAllocationResult(
        byte Status, ushort Stat = 0, ushort LevelPoints = 0, ushort PowerLevelPoints = 0);

    public sealed partial class WorldDatabase
    {
        public async Task<DateTime?> LoadPowerUserExpirationAsync(int userId)
        {
            await using var connection = new MySqlConnection(_conn);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "SELECT NULLIF(powertimedate,'0000-00-00 00:00:00') " +
                "FROM usergameinfo WHERE id=@u", connection);
            command.Parameters.AddWithValue("@u", userId);
            object? value = await command.ExecuteScalarAsync();
            return value == null || value is DBNull ? null : Convert.ToDateTime(value);
        }

        public async Task<PowerUserPurchaseResult> PurchasePowerUserAsync(
            PowerUserPurchaseCommand request)
        {
            if (request.UserId <= 0 || string.IsNullOrEmpty(request.AccountId) ||
                request.Mode > 1 || request.BasePrice <= 0 || request.BonusPoints < 0 ||
                request.DurationDays <= 0)
                return new PowerUserPurchaseResult(PowerUserPurchaseStatus.Failed);
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                PaymentResolution payment = await ResolvePaymentAsync(connection, transaction,
                    new PaymentQuoteRequest(request.UserId, request.Payment,
                        request.BasePrice, true));
                if (payment.FailureStatus != 0)
                    return new PowerUserPurchaseResult(
                        (PowerUserPurchaseStatus)payment.FailureStatus);
                return await CommitPowerUserPurchaseAsync(
                    connection, transaction, request, payment);
            }
            catch (Exception ex)
            {
                Log.Error("shop", "Power User transacional user={0}: {1}",
                    request.UserId, ex.Message);
                return new PowerUserPurchaseResult(PowerUserPurchaseStatus.Failed);
            }
        }

        private sealed record PowerUserState(int Gold, int Points, long PowerTime);

        private static async Task<PowerUserPurchaseResult> CommitPowerUserPurchaseAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            PowerUserPurchaseCommand request, PaymentResolution payment)
        {
            int cash = await LockPowerUserCashAsync(
                connection, transaction, request.AccountId);
            if (cash < payment.Cost)
                return new PowerUserPurchaseResult(
                    PowerUserPurchaseStatus.InsufficientCash, Cash: cash);
            PowerUserState? previous = await LockPowerUserStateAsync(
                connection, transaction, request.UserId);
            if (previous == null)
                return new PowerUserPurchaseResult(PowerUserPurchaseStatus.Failed, Cash: cash);

            long? couponLogId = await ConsumeCouponAsync(
                connection, transaction, request.UserId, payment,
                CharacterEconomyRules.PowerUserProduct(request.Mode));
            int nextCash = cash - payment.Cost;
            int nextPoints = checked(previous.Points + request.BonusPoints);
            uint powerTimeMarker = await UpdatePowerUserAsync(connection, transaction, request,
                nextCash, nextPoints);
            DateTime expiresAt = await ReadPowerUserExpirationAsync(
                connection, transaction, request.UserId);
            var ledger = new PowerUserLogEntry(request.UserId, request.Mode, payment.Cost,
                previous.PowerTime, powerTimeMarker, previous.Points, nextPoints,
                expiresAt, couponLogId);
            await InsertPowerUserLogAsync(connection, transaction, ledger);
            await transaction.CommitAsync();
            return new PowerUserPurchaseResult(PowerUserPurchaseStatus.Success,
                previous.Gold, nextCash, powerTimeMarker, nextPoints, expiresAt,
                payment.CouponRowId, payment.CouponItemId);
        }

        private static async Task<int> LockPowerUserCashAsync(
            MySqlConnection connection, MySqlTransaction transaction, string accountId)
        {
            await using var command = new MySqlCommand(
                "SELECT cash FROM cash WHERE id=@id FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("@id", accountId);
            object? value = await command.ExecuteScalarAsync();
            return value == null ? -1 : Convert.ToInt32(value);
        }

        private static async Task<PowerUserState?> LockPowerUserStateAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId)
        {
            await using var command = new MySqlCommand(
                "SELECT gold,powerlevelpoint,powertime FROM usergameinfo " +
                "WHERE id=@u FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("@u", userId);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            return new PowerUserState(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt64(2));
        }

        private static async Task<uint> UpdatePowerUserAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            PowerUserPurchaseCommand request, int cash, int points)
        {
            await ExecuteAsync(connection, transaction,
                "UPDATE cash SET cash=@cash WHERE id=@account",
                ("@cash", cash), ("@account", request.AccountId));
            string timeUpdate = request.Mode == 0
                ? "powertime=(TO_DAYS(NOW())*1440+HOUR(NOW())*60+MINUTE(NOW()))+@minutes," +
                  "powertimedate=DATE_ADD(NOW(),INTERVAL @days DAY)"
                : "powertime=powertime+@minutes," +
                  "powertimedate=DATE_ADD(powertimedate,INTERVAL @days DAY)";
            await ExecuteAsync(connection, transaction,
                "UPDATE usergameinfo SET powerlevelpoint=@points," + timeUpdate + " WHERE id=@u",
                ("@points", points), ("@minutes", request.DurationDays * 1440),
                ("@days", request.DurationDays), ("@u", request.UserId));
            await using var marker = new MySqlCommand(
                "SELECT powertime FROM usergameinfo WHERE id=@u", connection, transaction);
            marker.Parameters.AddWithValue("@u", request.UserId);
            return checked(Convert.ToUInt32(await marker.ExecuteScalarAsync()));
        }

        private static async Task<DateTime> ReadPowerUserExpirationAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId)
        {
            await using var command = new MySqlCommand(
                "SELECT powertimedate FROM usergameinfo WHERE id=@u", connection, transaction);
            command.Parameters.AddWithValue("@u", userId);
            object? value = await command.ExecuteScalarAsync();
            if (value == null || value is DBNull)
                throw new InvalidOperationException("Validade do Power User não foi persistida.");
            return Convert.ToDateTime(value);
        }

        private sealed record PowerUserLogEntry(
            int UserId, byte Mode, int Cost, long PreviousMarker, uint CurrentMarker,
            int PreviousPoints, int CurrentPoints, DateTime ExpiresAt, long? CouponLogId);

        private static Task InsertPowerUserLogAsync(
            MySqlConnection connection, MySqlTransaction transaction, PowerUserLogEntry entry) =>
            ExecuteAsync(connection, transaction,
                "INSERT INTO logbuypoweruser(userid,extend,buycash,powertime_prev," +
                "powertime_cur,buytime,powertime,powerlevelpoint_prev," +
                "powerlevelpoint_cur,coupon_log_id) VALUES(@u,@extend,@cash,@timePrev," +
                "@timeCur,NOW(),@expires,@pointsPrev,@pointsCur,@coupon)",
                ("@u", entry.UserId), ("@extend", entry.Mode),
                ("@cash", entry.Cost), ("@timePrev", entry.PreviousMarker),
                ("@timeCur", entry.CurrentMarker), ("@expires", entry.ExpiresAt),
                ("@pointsPrev", entry.PreviousPoints), ("@pointsCur", entry.CurrentPoints),
                ("@coupon", entry.CouponLogId?.ToString() ?? ""));

        public async Task<StatAllocationResult> AllocateStatTransactionalAsync(
            int userId, int characterId, byte statIndex)
        {
            string[] columns =
                ["hit1", "hit2", "hit3", "hit4", "chit", "hp", "ap", "attackspeed", "speed", "maxcp"];
            if (userId <= 0 || characterId <= 0 || statIndex >= columns.Length)
                return new StatAllocationResult(5);
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                return await CommitStatAllocationAsync(connection, transaction,
                    userId, characterId, columns[statIndex]);
            }
            catch (Exception ex)
            {
                Log.Error("shop", "alocação transacional user={0} char={1}: {2}",
                    userId, characterId, ex.Message);
                return new StatAllocationResult(1);
            }
        }

        private static async Task<StatAllocationResult> CommitStatAllocationAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            int userId, int characterId, string column)
        {
            (int Stat, int LevelPoints)? character = await LockAllocationCharacterAsync(
                connection, transaction, userId, characterId, column);
            if (character == null) return new StatAllocationResult(1);
            int powerPoints = await LockAllocationPowerPointsAsync(
                connection, transaction, userId);
            if (powerPoints < 0) return new StatAllocationResult(1);
            if (character.Value.Stat >= 50) return new StatAllocationResult(4);
            if (character.Value.LevelPoints <= 0 && powerPoints <= 0)
                return new StatAllocationResult(3);

            int levelPoints = character.Value.LevelPoints;
            if (levelPoints > 0) levelPoints--;
            else powerPoints--;
            await ExecuteAsync(connection, transaction,
                $"UPDATE characterinfo SET {column}={column}+1,levelpoint=@level WHERE id=@char",
                ("@level", levelPoints), ("@char", characterId));
            await ExecuteAsync(connection, transaction,
                "UPDATE usergameinfo SET powerlevelpoint=@power WHERE id=@u",
                ("@power", powerPoints), ("@u", userId));
            await transaction.CommitAsync();
            return new StatAllocationResult(0, checked((ushort)(character.Value.Stat + 1)),
                checked((ushort)levelPoints), checked((ushort)powerPoints));
        }

        private static async Task<(int Stat, int LevelPoints)?> LockAllocationCharacterAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            int userId, int characterId, string column)
        {
            await using var command = new MySqlCommand(
                $"SELECT {column},levelpoint FROM characterinfo " +
                "WHERE id=@char AND userid=@u FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("@char", characterId);
            command.Parameters.AddWithValue("@u", userId);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync()
                ? (reader.GetInt32(0), reader.GetInt32(1))
                : null;
        }

        private static async Task<int> LockAllocationPowerPointsAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId)
        {
            await using var command = new MySqlCommand(
                "SELECT powerlevelpoint FROM usergameinfo WHERE id=@u FOR UPDATE",
                connection, transaction);
            command.Parameters.AddWithValue("@u", userId);
            object? value = await command.ExecuteScalarAsync();
            return value == null ? -1 : Convert.ToInt32(value);
        }
    }
}
