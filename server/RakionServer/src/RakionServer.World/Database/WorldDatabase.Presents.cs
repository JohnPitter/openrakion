using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.World.Database
{
    public sealed partial class WorldDatabase
    {
        public async Task<PresentPeekResult> PeekPresentAsync(int userId)
        {
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var command = new MySqlCommand(
                    "SELECT id,present_id FROM pendingpresents WHERE user_id=@u ORDER BY id LIMIT 1",
                    connection);
                command.Parameters.AddWithValue("@u", userId);
                await using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return new PresentPeekResult(PresentPeekStatus.Empty);
                return new PresentPeekResult(
                    PresentPeekStatus.Success, reader.GetInt32(0), reader.GetInt32(1));
            }
            catch (Exception ex)
            {
                Log.Error("present", "peek user={0}: {1}", userId, ex.Message);
                return new PresentPeekResult(PresentPeekStatus.Empty);
            }
        }

        public async Task<PresentAcceptResult> AcceptPresentAsync(
            int userId, int pendingId, ushort slot, bool slotAvailable)
        {
            if (!slotAvailable) return new PresentAcceptResult(PresentAcceptStatus.SlotOccupied);
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                var first = await LoadFirstPresentForUpdateAsync(connection, transaction, userId);
                if (first == null) return new PresentAcceptResult(PresentAcceptStatus.Empty);
                if (first.Value.PendingId != pendingId)
                    return new PresentAcceptResult(PresentAcceptStatus.NotFirst);

                if (!await ItemExistsAsync(connection, transaction, first.Value.ItemId))
                    return new PresentAcceptResult(PresentAcceptStatus.Failed);

                await LockInventoryAsync(connection, transaction, userId);
                if (await IsStorageSlotOccupiedAsync(connection, transaction, userId, slot))
                    return new PresentAcceptResult(PresentAcceptStatus.SlotOccupied);

                await using (var count = new MySqlCommand(
                    "SELECT COUNT(DISTINCT slot) FROM useriteminfo " +
                    "WHERE userid=@u AND characterid=0",
                    connection, transaction))
                {
                    count.Parameters.AddWithValue("@u", userId);
                    if (Convert.ToInt32(await count.ExecuteScalarAsync()) >= 120)
                        return new PresentAcceptResult(PresentAcceptStatus.SlotOccupied);
                }

                int rowId = await InsertInventoryItemAsync(connection, transaction,
                    userId, first.Value.ItemId, slot);

                await RemovePendingPresentAsync(connection, transaction, pendingId, userId);
                await using (var audit = new MySqlCommand(
                    "UPDATE logpresent SET accept_time=NOW() WHERE pending_id=@id AND user_id=@u",
                    connection, transaction))
                {
                    audit.Parameters.AddWithValue("@id", pendingId);
                    audit.Parameters.AddWithValue("@u", userId);
                    await audit.ExecuteNonQueryAsync();
                }
                await transaction.CommitAsync();
                return new PresentAcceptResult(
                    PresentAcceptStatus.Success, rowId, first.Value.ItemId, slot);
            }
            catch (Exception ex)
            {
                Log.Error("present", "accept user={0} pending={1} slot={2}: {3}",
                    userId, pendingId, slot, ex.Message);
                return new PresentAcceptResult(PresentAcceptStatus.Failed);
            }
        }

        public async Task<PresentDisposeResult> DisposePresentAsync(int userId, int pendingId)
        {
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                var first = await LoadFirstPresentForUpdateAsync(connection, transaction, userId);
                if (first == null) return new PresentDisposeResult(PresentDisposeStatus.Empty);
                if (first.Value.PendingId != pendingId)
                    return new PresentDisposeResult(PresentDisposeStatus.NotFirst);

                await RemovePendingPresentAsync(connection, transaction, pendingId, userId);
                await using (var audit = new MySqlCommand(
                    "UPDATE logpresent SET dispose_time=NOW() WHERE pending_id=@id AND user_id=@u",
                    connection, transaction))
                {
                    audit.Parameters.AddWithValue("@id", pendingId);
                    audit.Parameters.AddWithValue("@u", userId);
                    await audit.ExecuteNonQueryAsync();
                }
                await transaction.CommitAsync();
                return new PresentDisposeResult(PresentDisposeStatus.Success);
            }
            catch (Exception ex)
            {
                Log.Error("present", "dispose user={0} pending={1}: {2}", userId, pendingId, ex.Message);
                return new PresentDisposeResult(PresentDisposeStatus.Failed);
            }
        }

        private static async Task<(int PendingId, int ItemId)?> LoadFirstPresentForUpdateAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId)
        {
            await using var command = new MySqlCommand(
                "SELECT id,present_id FROM pendingpresents WHERE user_id=@u ORDER BY id LIMIT 1 FOR UPDATE",
                connection, transaction);
            command.Parameters.AddWithValue("@u", userId);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            return (reader.GetInt32(0), reader.GetInt32(1));
        }

        private static async Task<bool> ItemExistsAsync(
            MySqlConnection connection, MySqlTransaction transaction, int itemId)
        {
            await using var command = new MySqlCommand(
                "SELECT 1 FROM iteminfo WHERE id=@item LIMIT 1", connection, transaction);
            command.Parameters.AddWithValue("@item", itemId);
            return await command.ExecuteScalarAsync() != null;
        }

        private static async Task<bool> IsStorageSlotOccupiedAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId, ushort slot)
        {
            await using var command = new MySqlCommand(
                "SELECT 1 FROM useriteminfo " +
                "WHERE userid=@u AND characterid=0 AND slot=@slot LIMIT 1",
                connection, transaction);
            command.Parameters.AddWithValue("@u", userId);
            command.Parameters.AddWithValue("@slot", slot);
            return await command.ExecuteScalarAsync() != null;
        }

        private static async Task RemovePendingPresentAsync(
            MySqlConnection connection, MySqlTransaction transaction, int pendingId, int userId)
        {
            await using var command = new MySqlCommand(
                "DELETE FROM pendingpresents WHERE id=@id AND user_id=@u", connection, transaction);
            command.Parameters.AddWithValue("@id", pendingId);
            command.Parameters.AddWithValue("@u", userId);
            if (await command.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("O presente mudou durante a transacao.");
        }
    }
}
