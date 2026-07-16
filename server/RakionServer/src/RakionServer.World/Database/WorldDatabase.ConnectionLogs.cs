using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.World.Database
{
    public sealed record ConnectionLogStart(
        int UserId,
        string UserName,
        int ServerId,
        string Ip,
        int Country);

    public sealed partial class WorldDatabase
    {
        public async Task<int> LogUserConnectAsync(ConnectionLogStart start)
        {
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var command = new MySqlCommand(
                    "INSERT INTO loguserconnect " +
                    "(userid, username, serverid, userip, country, connecttime) " +
                    "VALUES (@uid, @uname, @sid, @ip, @country, NOW())", connection);
                command.Parameters.AddWithValue("@uid", start.UserId);
                command.Parameters.AddWithValue("@uname", start.UserName);
                command.Parameters.AddWithValue("@sid", start.ServerId);
                command.Parameters.AddWithValue("@ip", start.Ip);
                command.Parameters.AddWithValue("@country", start.Country);
                await command.ExecuteNonQueryAsync();
                int connectionLogId = checked((int)command.LastInsertedId);
                Log.Debug("db", "loguserconnect: id={0} uid={1} '{2}' @ {3}",
                    connectionLogId, start.UserId, start.UserName, start.Ip);
                return connectionLogId;
            }
            catch (Exception ex)
            {
                Log.Error("db", "LogUserConnectAsync: {0}", ex.Message);
                return 0;
            }
        }

        public async Task UpdateConnectionRealIpAsync(int connectionLogId, string advertisedIp)
        {
            if (connectionLogId <= 0) return;
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var command = new MySqlCommand(
                    "UPDATE loguserconnect SET RealIP=@ip WHERE id=@id", connection);
                command.Parameters.AddWithValue("@ip", advertisedIp);
                command.Parameters.AddWithValue("@id", connectionLogId);
                await command.ExecuteNonQueryAsync();
                Log.Debug("db", "loguserconnect: id={0} RealIP={1}",
                    connectionLogId, advertisedIp);
            }
            catch (Exception ex)
            {
                Log.Error("db", "UpdateConnectionRealIpAsync({0}): {1}",
                    connectionLogId, ex.Message);
            }
        }

        public async Task CloseConnectionLogAsync(int connectionLogId, ushort reason)
        {
            if (connectionLogId <= 0) return;
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var command = new MySqlCommand(
                    "UPDATE loguserconnect " +
                    "SET disconnecttime=NOW(), note=@reason WHERE id=@id", connection);
                command.Parameters.AddWithValue("@reason", reason);
                command.Parameters.AddWithValue("@id", connectionLogId);
                await command.ExecuteNonQueryAsync();
                Log.Debug("db", "loguserconnect: id={0} disconnect reason={1}",
                    connectionLogId, reason);
            }
            catch (Exception ex)
            {
                Log.Error("db", "CloseConnectionLogAsync({0}): {1}",
                    connectionLogId, ex.Message);
            }
        }
    }
}
