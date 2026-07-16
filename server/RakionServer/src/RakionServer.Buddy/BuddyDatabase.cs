using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.Buddy
{
    public sealed record BuddyAccount(string AccountId, string Password, string DisplayName);
    public sealed record BuddyChatState(DateTime MutedUntilUtc, IReadOnlyList<string> BlockedAccounts);

    public sealed partial class BuddyDatabase
    {
        private readonly string _connectionString;

        public BuddyDatabase(string connectionString) => _connectionString = connectionString;

        public async Task EnsureSchemaAsync()
        {
            await using MySqlConnection connection = await OpenAsync();
            await ExecuteAsync(connection,
                "CREATE TABLE IF NOT EXISTS buddy_sms(" +
                "id INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY," +
                "sender_account VARCHAR(16) NOT NULL,target_account VARCHAR(16) NOT NULL," +
                "message VARBINARY(128) NOT NULL,created_at DATETIME(6) NOT NULL," +
                "delivered_at DATETIME(6) NULL,acked_at DATETIME(6) NULL," +
                "INDEX ix_buddy_sms_pending(target_account,acked_at,id)) ENGINE=InnoDB");
            await ExecuteAsync(connection,
                "CREATE TABLE IF NOT EXISTS chat_mute(" +
                "account_id VARCHAR(16) NOT NULL PRIMARY KEY,muted_until DATETIME(6) NOT NULL," +
                "reason VARCHAR(64) NOT NULL,operator_id VARCHAR(32) NOT NULL," +
                "updated_at DATETIME(6) NOT NULL) ENGINE=InnoDB");
            await ExecuteAsync(connection,
                "CREATE TABLE IF NOT EXISTS chat_block(" +
                "owner_account_id VARCHAR(16) NOT NULL,blocked_account_id VARCHAR(16) NOT NULL," +
                "PRIMARY KEY(owner_account_id,blocked_account_id)) ENGINE=InnoDB");
            await ExecuteAsync(connection,
                "CREATE TABLE IF NOT EXISTS chat_moderation_log(" +
                "id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,sender_account VARCHAR(16) NOT NULL," +
                "target_account VARCHAR(16) NOT NULL,scope TINYINT UNSIGNED NOT NULL," +
                "action TINYINT UNSIGNED NOT NULL,rule VARCHAR(128) NOT NULL," +
                "original_length INT UNSIGNED NOT NULL,result_length INT UNSIGNED NOT NULL," +
                "content_sha256 CHAR(64) NOT NULL,created_at DATETIME(6) NOT NULL," +
                "INDEX ix_chat_moderation_sender(sender_account,created_at)) ENGINE=InnoDB");
            await EnsureFriendSchemaAsync(connection);
        }

        public async Task<BuddyAccount?> LoadAccountAsync(string accountId)
        {
            await using MySqlConnection connection = await OpenAsync();
            await using var command = new MySqlCommand(
                "SELECT u.password,COALESCE(NULLIF(g.buddyname,''),NULLIF(g.charname,''),u.id) " +
                "FROM user u LEFT JOIN usergameinfo g ON g.name=u.id WHERE u.id=@id LIMIT 1", connection);
            command.Parameters.AddWithValue("@id", accountId);
            await using MySqlDataReader reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync()
                ? new BuddyAccount(accountId, reader.GetString(0), reader.GetString(1))
                : null;
        }

        public async Task<BuddyChatState> LoadChatStateAsync(string accountId)
        {
            await using MySqlConnection connection = await OpenAsync();
            DateTime muted = DateTime.MinValue;
            await using (var command = new MySqlCommand(
                "SELECT muted_until FROM chat_mute WHERE account_id=@id AND muted_until>UTC_TIMESTAMP(6)", connection))
            {
                command.Parameters.AddWithValue("@id", accountId);
                object? value = await command.ExecuteScalarAsync();
                if (value is DateTime date) muted = DateTime.SpecifyKind(date, DateTimeKind.Utc);
            }
            var blocked = new List<string>();
            await using (var command = new MySqlCommand(
                "SELECT blocked_account_id FROM chat_block WHERE owner_account_id=@id", connection))
            {
                command.Parameters.AddWithValue("@id", accountId);
                await using MySqlDataReader reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) blocked.Add(reader.GetString(0));
            }
            return new BuddyChatState(muted, blocked);
        }

        public async Task<BuddySmsMessage?> QueueSmsAsync(
            BuddyAccount sender, string targetAccount, string text)
        {
            BuddyAccount? target = await LoadAccountAsync(targetAccount);
            if (target == null) return null;
            DateTime now = DateTime.UtcNow;
            await using MySqlConnection connection = await OpenAsync();
            await using var command = new MySqlCommand(
                "INSERT INTO buddy_sms(sender_account,target_account,message,created_at) " +
                "VALUES(@sender,@target,@message,@created)", connection);
            command.Parameters.AddWithValue("@sender", sender.AccountId);
            command.Parameters.AddWithValue("@target", targetAccount);
            command.Parameters.AddWithValue("@message", Encoding.Latin1.GetBytes(text));
            command.Parameters.AddWithValue("@created", now);
            await command.ExecuteNonQueryAsync();
            uint id = checked((uint)command.LastInsertedId);
            return new BuddySmsMessage(id, sender.AccountId, sender.DisplayName,
                targetAccount, text, now);
        }

        public async Task<IReadOnlyList<BuddySmsMessage>> LoadPendingSmsAsync(
            string targetAccount, int limit = 50)
        {
            await using MySqlConnection connection = await OpenAsync();
            await using var command = new MySqlCommand(
                "SELECT s.id,s.sender_account,COALESCE(NULLIF(g.buddyname,''),NULLIF(g.charname,''),s.sender_account)," +
                "s.message,s.created_at FROM buddy_sms s LEFT JOIN usergameinfo g ON g.name=s.sender_account " +
                "WHERE s.target_account=@target AND s.acked_at IS NULL ORDER BY s.id LIMIT @limit", connection);
            command.Parameters.AddWithValue("@target", targetAccount);
            command.Parameters.AddWithValue("@limit", limit);
            var messages = new List<BuddySmsMessage>();
            await using MySqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                messages.Add(new BuddySmsMessage(reader.GetUInt32(0), reader.GetString(1),
                    reader.GetString(2), targetAccount,
                    Encoding.Latin1.GetString((byte[])reader[3]),
                    DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc)));
            return messages;
        }

        public async Task MarkDeliveredAsync(string targetAccount, IReadOnlyList<uint> ids)
        {
            if (ids.Count == 0) return;
            await UpdateSmsIdsAsync(targetAccount, ids,
                "UPDATE buddy_sms SET delivered_at=UTC_TIMESTAMP(6) " +
                "WHERE target_account=@target AND id IN ({0}) AND delivered_at IS NULL");
        }

        public async Task AcknowledgeAsync(string targetAccount, IReadOnlyList<uint> ids)
        {
            if (ids.Count == 0) return;
            await UpdateSmsIdsAsync(targetAccount, ids,
                "UPDATE buddy_sms SET acked_at=UTC_TIMESTAMP(6) " +
                "WHERE target_account=@target AND id IN ({0}) AND acked_at IS NULL");
        }

        public async Task SaveAutomaticMuteAsync(string accountId, DateTime untilUtc, string reason)
        {
            await using MySqlConnection connection = await OpenAsync();
            await using var command = new MySqlCommand(
                "INSERT INTO chat_mute(account_id,muted_until,reason,operator_id,updated_at) " +
                "VALUES(@id,@until,@reason,'automatic',UTC_TIMESTAMP(6)) ON DUPLICATE KEY UPDATE " +
                "muted_until=GREATEST(muted_until,VALUES(muted_until)),reason=VALUES(reason)," +
                "operator_id='automatic',updated_at=UTC_TIMESTAMP(6)", connection);
            command.Parameters.AddWithValue("@id", accountId);
            command.Parameters.AddWithValue("@until", untilUtc);
            command.Parameters.AddWithValue("@reason", reason);
            await command.ExecuteNonQueryAsync();
        }

        public async Task AuditAsync(
            string sender, string target, ChatModerationDecision decision, string original)
        {
            if (decision.Action == ChatModerationAction.Allowed) return;
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original)));
            await using MySqlConnection connection = await OpenAsync();
            await using var command = new MySqlCommand(
                "INSERT INTO chat_moderation_log(sender_account,target_account,scope,action,rule," +
                "original_length,result_length,content_sha256,created_at) " +
                "VALUES(@sender,@target,@scope,@action,@rule,@before,@after,@hash,UTC_TIMESTAMP(6))", connection);
            command.Parameters.AddWithValue("@sender", sender);
            command.Parameters.AddWithValue("@target", target);
            command.Parameters.AddWithValue("@scope", (byte)ChatScope.Sms);
            command.Parameters.AddWithValue("@action", (byte)decision.Action);
            command.Parameters.AddWithValue("@rule", decision.Rule);
            command.Parameters.AddWithValue("@before", original.Length);
            command.Parameters.AddWithValue("@after", decision.Text.Length);
            command.Parameters.AddWithValue("@hash", hash);
            await command.ExecuteNonQueryAsync();
        }

        private async Task UpdateSmsIdsAsync(
            string targetAccount, IReadOnlyList<uint> ids, string sqlTemplate)
        {
            await using MySqlConnection connection = await OpenAsync();
            var names = new string[ids.Count];
            await using var command = new MySqlCommand { Connection = connection };
            command.Parameters.AddWithValue("@target", targetAccount);
            for (int i = 0; i < ids.Count; i++)
            {
                names[i] = $"@id{i}";
                command.Parameters.AddWithValue(names[i], ids[i]);
            }
            command.CommandText = string.Format(sqlTemplate, string.Join(',', names));
            await command.ExecuteNonQueryAsync();
        }

        private async Task<MySqlConnection> OpenAsync()
        {
            var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            return connection;
        }

        private static async Task ExecuteAsync(MySqlConnection connection, string sql)
        {
            await using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
