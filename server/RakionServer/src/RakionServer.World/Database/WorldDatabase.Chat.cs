using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.World.Database
{
    public sealed record ChatPersistenceState(
        DateTime MutedUntilUtc, IReadOnlyList<string> BlockedAccounts);

    public sealed partial class WorldDatabase
    {
        public async Task<ChatPersistenceState> LoadChatStateAsync(string accountId)
        {
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                DateTime mutedUntil = await ReadMuteAsync(connection, accountId);
                IReadOnlyList<string> blocked = await ReadBlocksAsync(connection, accountId);
                return new ChatPersistenceState(mutedUntil, blocked);
            }
            catch (Exception ex)
            {
                Log.Error("chat", "falha ao carregar moderação de '{0}': {1}",
                    accountId, ex.Message);
                return new ChatPersistenceState(DateTime.MinValue, []);
            }
        }

        private static async Task<DateTime> ReadMuteAsync(
            MySqlConnection connection, string accountId)
        {
            await using var command = new MySqlCommand(
                "SELECT muted_until FROM chat_mute WHERE account_id=@account " +
                "AND muted_until>UTC_TIMESTAMP(6)", connection);
            command.Parameters.AddWithValue("@account", accountId);
            object? value = await command.ExecuteScalarAsync();
            return value == null
                ? DateTime.MinValue
                : DateTime.SpecifyKind(Convert.ToDateTime(value), DateTimeKind.Utc);
        }

        private static async Task<IReadOnlyList<string>> ReadBlocksAsync(
            MySqlConnection connection, string accountId)
        {
            await using var command = new MySqlCommand(
                "SELECT blocked_account_id FROM chat_block WHERE owner_account_id=@account",
                connection);
            command.Parameters.AddWithValue("@account", accountId);
            var blocked = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) blocked.Add(reader.GetString(0));
            return blocked;
        }

        public async Task SaveAutomaticMuteAsync(
            string accountId, DateTime mutedUntilUtc, string reason)
        {
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var command = new MySqlCommand(
                    "INSERT INTO chat_mute(account_id,muted_until,reason,operator_id,updated_at) " +
                    "VALUES(@account,@until,@reason,'automatic',UTC_TIMESTAMP(6)) " +
                    "ON DUPLICATE KEY UPDATE muted_until=GREATEST(muted_until,@until)," +
                    "reason=@reason,operator_id='automatic',updated_at=UTC_TIMESTAMP(6)", connection);
                command.Parameters.AddWithValue("@account", accountId);
                command.Parameters.AddWithValue("@until", mutedUntilUtc);
                command.Parameters.AddWithValue("@reason", reason);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Log.Error("chat", "falha ao persistir mute de '{0}': {1}",
                    accountId, ex.Message);
            }
        }

        public async Task AuditChatDecisionAsync(
            string sender, string target, ChatScope scope,
            ChatModerationDecision decision, int originalLength)
        {
            if (decision.Action == ChatModerationAction.Allowed) return;
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var command = new MySqlCommand(
                    "INSERT INTO chat_moderation_log(sender_account,target_account,scope,action," +
                    "rule_id,text_hash,length_before,length_after,created_at) " +
                    "VALUES(@sender,@target,@scope,@action,@rule,@hash,@before,@after,UTC_TIMESTAMP(6))",
                    connection);
                command.Parameters.AddWithValue("@sender", sender);
                command.Parameters.AddWithValue("@target", target);
                command.Parameters.AddWithValue("@scope", (byte)scope);
                command.Parameters.AddWithValue("@action", (byte)decision.Action);
                command.Parameters.AddWithValue("@rule", decision.Rule);
                command.Parameters.AddWithValue("@hash", HashText(decision.Text));
                command.Parameters.AddWithValue("@before", originalLength);
                command.Parameters.AddWithValue("@after", decision.Text.Length);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Log.Error("chat", "falha na auditoria sender='{0}': {1}", sender, ex.Message);
            }
        }

        private static string HashText(string text) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
