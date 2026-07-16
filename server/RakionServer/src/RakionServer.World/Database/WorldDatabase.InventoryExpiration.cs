using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.World.Database
{
    public sealed partial class WorldDatabase
    {
        public async Task<int> PurgeExpiredInventoryAsync(int userId)
        {
            if (userId <= 0) return -1;
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var command = new MySqlCommand(
                    "DELETE FROM useriteminfo WHERE userid=@u AND limittime>0 " +
                    "AND limittime<((TO_DAYS(NOW())*24+HOUR(NOW()))*60+MINUTE(NOW()))",
                    connection);
                command.Parameters.AddWithValue("@u", userId);
                int removed = await command.ExecuteNonQueryAsync();
                if (removed > 0)
                    Log.Info("inventory", "expiração removeu {0} item(ns) do user {1}", removed, userId);
                return removed;
            }
            catch (Exception ex)
            {
                Log.Error("inventory", "PurgeExpiredInventoryAsync({0}): {1}", userId, ex.Message);
                return -1;
            }
        }
    }
}
