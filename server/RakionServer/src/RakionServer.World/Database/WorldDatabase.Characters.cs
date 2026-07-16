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
        private const int RenameCost = 3000;

        public async Task<CharacterSelectResult> SelectCharacterAsync(int userId, int characterId)
        {
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
                CharacterInfo? character = await FindCharacterForSelectionAsync(
                    connection, transaction, userId, characterId);
                if (character == null)
                {
                    await transaction.RollbackAsync();
                    return new CharacterSelectResult(CharacterSelectStatus.NotFound);
                }

                await using var update = new MySqlCommand(
                    "UPDATE usergameinfo SET charname=@name WHERE id=@u", connection, transaction);
                update.Parameters.AddWithValue("@u", userId);
                update.Parameters.AddWithValue("@name", character.Name);
                await update.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
                character.Used = true;
                return new CharacterSelectResult(CharacterSelectStatus.Success, character);
            }
            catch (Exception ex)
            {
                Log.Error("db", "SelectCharacterAsync(user={0}, char={1}): {2}",
                    userId, characterId, ex.Message);
                return new CharacterSelectResult(CharacterSelectStatus.SystemError);
            }
        }

        private static async Task<CharacterInfo?> FindCharacterForSelectionAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId, int characterId)
        {
            await using var command = new MySqlCommand(
                "SELECT " + CharSelectColumns + " FROM characterinfo " +
                "WHERE userid=@u AND id=@id AND auth<>10 LIMIT 1 FOR UPDATE",
                connection, transaction);
            command.Parameters.AddWithValue("@u", userId);
            command.Parameters.AddWithValue("@id", characterId);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? MapCharacter(reader) : null;
        }

        public async Task<CharacterCreateResult> CreateCharacterAsync(
            int userId, string name, byte charClass, byte slot)
        {
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);

                if (!await AccountExistsAsync(connection, transaction, userId))
                    return await RollbackAsync(transaction, CharacterCreateStatus.Failed);
                if (await CharacterExistsAsync(connection, transaction, "userid=@u AND auth<>10 AND slot=@slot", userId, slot, name))
                    return await RollbackAsync(transaction, CharacterCreateStatus.SlotOccupied);
                if (await CharacterExistsAsync(connection, transaction, "name=@name", userId, slot, name))
                    return await RollbackAsync(transaction, CharacterCreateStatus.DuplicateName);

                await using var insert = new MySqlCommand(
                    "INSERT INTO characterinfo(name,userid,class,slot,createtime,changetime) " +
                    "VALUES(@name,@u,@class,@slot,NOW(),NOW())", connection, transaction);
                insert.Parameters.AddWithValue("@name", name);
                insert.Parameters.AddWithValue("@u", userId);
                insert.Parameters.AddWithValue("@class", charClass);
                insert.Parameters.AddWithValue("@slot", slot);
                await insert.ExecuteNonQueryAsync();
                int characterId = checked((int)insert.LastInsertedId);

                await using var activate = new MySqlCommand(
                    "UPDATE usergameinfo SET charname=@name WHERE charname='' AND id=@u", connection, transaction);
                activate.Parameters.AddWithValue("@name", name);
                activate.Parameters.AddWithValue("@u", userId);
                await activate.ExecuteNonQueryAsync();
                await transaction.CommitAsync();

                return new CharacterCreateResult(CharacterCreateStatus.Success, new CharacterInfo
                {
                    Id = characterId,
                    UserId = userId,
                    Name = name,
                    Class = charClass,
                    Slot = slot,
                    Level = 1
                });
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                return new CharacterCreateResult(CharacterCreateStatus.DuplicateName);
            }
            catch (Exception ex)
            {
                Log.Error("db", "CreateCharacterAsync(user={0}, name='{1}', class={2}, slot={3}): {4}",
                    userId, name, charClass, slot, ex.Message);
                return new CharacterCreateResult(CharacterCreateStatus.Failed);
            }
        }

        private static async Task<bool> AccountExistsAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId)
        {
            await using var command = new MySqlCommand(
                "SELECT 1 FROM usergameinfo WHERE id=@u LIMIT 1 FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("@u", userId);
            return await command.ExecuteScalarAsync() != null;
        }

        private static async Task<bool> CharacterExistsAsync(MySqlConnection connection,
            MySqlTransaction transaction, string predicate, int userId, byte slot, string name)
        {
            await using var command = new MySqlCommand(
                $"SELECT 1 FROM characterinfo WHERE {predicate} LIMIT 1 FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("@u", userId);
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@name", name);
            return await command.ExecuteScalarAsync() != null;
        }

        private static async Task<CharacterCreateResult> RollbackAsync(
            MySqlTransaction transaction, CharacterCreateStatus status)
        {
            await transaction.RollbackAsync();
            return new CharacterCreateResult(status);
        }

        public async Task<CharacterStateClearResult> ClearCharacterStateAsync(
            int userId, string accountId, int characterId, CharacterPayment payment)
        {
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
                var state = await LoadStateClearAsync(connection, transaction, userId, accountId, characterId);
                if (state == null)
                    return await RollbackStateClearAsync(transaction, CharacterStateClearStatus.Failed);
                int baseCost = CharacterEconomyRules.StateClearCost(state.Level);
                var paymentResult = await ResolvePaymentAsync(connection, transaction,
                    new PaymentQuoteRequest(userId, payment, baseCost, true));
                if (paymentResult.FailureStatus != 0)
                    return await RollbackStateClearAsync(
                        transaction, (CharacterStateClearStatus)paymentResult.FailureStatus);
                int allocated = state.StatsTotal;
                if (allocated == 0)
                    return await RollbackStateClearAsync(transaction, CharacterStateClearStatus.NoAllocatedStats);
                if (state.Cash < paymentResult.Cost)
                    return await RollbackStateClearAsync(transaction, CharacterStateClearStatus.InsufficientCash);
                int baseLevelPoint = Math.Max(0, (state.Level - 1) * 3);
                int totalLevelPoint = state.LevelPoint + allocated;
                int returnedPower = Math.Max(0, totalLevelPoint - baseLevelPoint);
                int newPower = state.PowerLevelPoint + returnedPower;
                int newCash = state.Cash - paymentResult.Cost;

                await ExecuteAsync(connection, transaction,
                    "UPDATE characterinfo SET levelpoint=@lp,hit1=0,hit2=0,hit3=0,hit4=0,chit=0," +
                    "hp=0,ap=0,attackspeed=0,speed=0,maxcp=0 WHERE id=@char AND userid=@u",
                    ("@lp", baseLevelPoint), ("@char", characterId), ("@u", userId));
                await ExecuteAsync(connection, transaction,
                    "UPDATE usergameinfo SET powerlevelpoint=@power WHERE id=@u",
                    ("@power", newPower), ("@u", userId));
                await ExecuteAsync(connection, transaction,
                    "UPDATE cash SET cash=@cash WHERE id=@account",
                    ("@cash", newCash), ("@account", accountId));
                long? couponLogId = await ConsumeCouponAsync(connection, transaction, userId,
                    paymentResult, CharacterEconomyRules.StateClearProduct(state.Level));
                await ExecuteAsync(connection, transaction,
                    "INSERT INTO logcharstateclear(userid,charid,level,cost,cash_prev,cash_cur," +
                    "totallevelpoint,usedpowerlevelpoint,createtime,coupon_log_id) " +
                    "VALUES(@u,@char,@level,@cost,@prev,@cur,@total,@returned,NOW(),@coupon)",
                    ("@u", userId), ("@char", characterId), ("@level", state.Level),
                    ("@cost", paymentResult.Cost),
                    ("@prev", state.Cash), ("@cur", newCash), ("@total", totalLevelPoint),
                    ("@returned", returnedPower), ("@coupon", couponLogId?.ToString() ?? ""));
                int[] presents = await GrantRandomPresentAsync(connection, transaction, userId,
                    state.Class, paymentResult.Cost, payment.UsesCoupon);
                await transaction.CommitAsync();
                return new CharacterStateClearResult(CharacterStateClearStatus.Success, newCash,
                    checked((ushort)baseLevelPoint), checked((ushort)newPower), presents);
            }
            catch (Exception ex)
            {
                Log.Error("db", "ClearCharacterStateAsync(user={0}, char={1}): {2}", userId, characterId, ex.Message);
                return new CharacterStateClearResult(CharacterStateClearStatus.Failed);
            }
        }

        public async Task<CharacterRenameResult> RenameCharacterAsync(
            int userId, string accountId, int characterId, string newName, CharacterPayment payment)
        {
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
                var paymentResult = await ResolvePaymentAsync(connection, transaction,
                    new PaymentQuoteRequest(userId, payment, RenameCost, true));
                if (paymentResult.FailureStatus != 0)
                    return await RollbackRenameAsync(
                        transaction, (CharacterRenameStatus)paymentResult.FailureStatus);
                if (await CharacterExistsAsync(connection, transaction, "name=@name", userId, 0, newName))
                    return await RollbackRenameAsync(transaction, CharacterRenameStatus.DuplicateName);

                var current = await LoadRenameAsync(connection, transaction, userId, accountId, characterId);
                if (current == null)
                    return await RollbackRenameAsync(transaction, CharacterRenameStatus.Failed);
                if (current.Value.Cash < paymentResult.Cost)
                    return await RollbackRenameAsync(transaction, CharacterRenameStatus.InsufficientCash);
                int newCash = current.Value.Cash - paymentResult.Cost;

                await ExecuteAsync(connection, transaction,
                    "UPDATE characterinfo SET name=@name WHERE id=@char AND userid=@u",
                    ("@name", newName), ("@char", characterId), ("@u", userId));
                await ExecuteAsync(connection, transaction,
                    "UPDATE cash SET cash=@cash WHERE id=@account",
                    ("@cash", newCash), ("@account", accountId));
                await ExecuteAsync(connection, transaction,
                    "UPDATE usergameinfo SET charname=@name WHERE id=@u AND charname=@old",
                    ("@name", newName), ("@u", userId), ("@old", current.Value.Name));
                long? couponLogId = await ConsumeCouponAsync(
                    connection, transaction, userId, paymentResult, 10005);
                await ExecuteAsync(connection, transaction,
                    "INSERT INTO logchangecharname(userid,charid,cost,cash_prev,cash_cur,charname_prev," +
                    "createtime,coupon_log_id) VALUES(@u,@char,@cost,@prev,@cur,@old,NOW(),@coupon)",
                    ("@u", userId), ("@char", characterId), ("@cost", paymentResult.Cost),
                    ("@prev", current.Value.Cash), ("@cur", newCash), ("@old", current.Value.Name),
                    ("@coupon", couponLogId?.ToString() ?? ""));
                int[] presents = await GrantRandomPresentAsync(connection, transaction, userId,
                    current.Value.Class, paymentResult.Cost, payment.UsesCoupon);
                await transaction.CommitAsync();
                return new CharacterRenameResult(CharacterRenameStatus.Success, newCash, newName, presents);
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                return new CharacterRenameResult(CharacterRenameStatus.DuplicateName);
            }
            catch (Exception ex)
            {
                Log.Error("db", "RenameCharacterAsync(user={0}, char={1}, name='{2}'): {3}",
                    userId, characterId, newName, ex.Message);
                return new CharacterRenameResult(CharacterRenameStatus.Failed);
            }
        }

        private sealed record StateClearData(
            int Level, byte Class, int LevelPoint, int StatsTotal, int PowerLevelPoint, int Cash);

        private static async Task<StateClearData?> LoadStateClearAsync(MySqlConnection connection,
            MySqlTransaction transaction, int userId, string accountId, int characterId)
        {
            await using var command = new MySqlCommand(
                "SELECT c.level,c.class,c.levelpoint,c.hit1+c.hit2+c.hit3+c.hit4+c.chit+c.hp+c.ap+" +
                "c.attackspeed+c.speed+c.maxcp,g.powerlevelpoint,ca.cash FROM characterinfo c " +
                "JOIN usergameinfo g ON g.id=c.userid JOIN cash ca ON ca.id=@account " +
                "WHERE c.id=@char AND c.userid=@u LIMIT 1 FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("@account", accountId);
            command.Parameters.AddWithValue("@char", characterId);
            command.Parameters.AddWithValue("@u", userId);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync()
                ? new StateClearData(reader.GetInt32(0), reader.GetByte(1), reader.GetInt32(2),
                    reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5))
                : null;
        }

        private static async Task<(string Name, int Cash, byte Class)?> LoadRenameAsync(MySqlConnection connection,
            MySqlTransaction transaction, int userId, string accountId, int characterId)
        {
            await using var command = new MySqlCommand(
                "SELECT c.name,ca.cash,c.class FROM characterinfo c JOIN cash ca ON ca.id=@account " +
                "WHERE c.id=@char AND c.userid=@u LIMIT 1 FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("@account", accountId);
            command.Parameters.AddWithValue("@char", characterId);
            command.Parameters.AddWithValue("@u", userId);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync()
                ? (reader.GetString(0), reader.GetInt32(1), reader.GetByte(2))
                : null;
        }

        private static async Task ExecuteAsync(MySqlConnection connection, MySqlTransaction transaction,
            string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = new MySqlCommand(sql, connection, transaction);
            foreach (var parameter in parameters)
                command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<CharacterStateClearResult> RollbackStateClearAsync(
            MySqlTransaction transaction, CharacterStateClearStatus status)
        {
            await transaction.RollbackAsync();
            return new CharacterStateClearResult(status);
        }

        private static async Task<CharacterRenameResult> RollbackRenameAsync(
            MySqlTransaction transaction, CharacterRenameStatus status)
        {
            await transaction.RollbackAsync();
            return new CharacterRenameResult(status);
        }
    }
}
