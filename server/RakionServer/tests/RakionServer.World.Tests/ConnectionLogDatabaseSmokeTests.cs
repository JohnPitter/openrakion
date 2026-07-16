using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.World;
using RakionServer.World.Database;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class ConnectionLogDatabaseSmokeTests
{
    [Fact]
    public async Task UdpPort1UpdatesAdvertisedRealIpByConnectionLogId()
    {
        string? value = Environment.GetEnvironmentVariable("RAKION_MYSQL_SMOKE_CONNECTION");
        if (string.IsNullOrWhiteSpace(value)) return;

        var admin = new MySqlConnectionStringBuilder(value);
        string databaseName = "rakion_connection_log_test_" + Guid.NewGuid().ToString("N");
        var scoped = new MySqlConnectionStringBuilder(admin.ConnectionString) { Database = databaseName };
        admin.Database = "";
        await ExecuteAsync(admin.ConnectionString, $"CREATE DATABASE `{databaseName}`");
        try
        {
            await ExecuteAsync(scoped.ConnectionString,
                "CREATE TABLE loguserconnect (id INT PRIMARY KEY AUTO_INCREMENT," +
                "userid INT NOT NULL,username VARCHAR(30),serverid INT NOT NULL," +
                "RealIP VARCHAR(15),userip VARCHAR(15),country INT NOT NULL," +
                "note INT,connecttime DATETIME NOT NULL,disconnecttime DATETIME) ENGINE=InnoDB");
            var database = new WorldDatabase(DbConfig(scoped));

            int id = await database.LogUserConnectAsync(new ConnectionLogStart(
                7, "account", 1, "198.51.100.10", 76));
            Assert.True(id > 0);
            Assert.Null(await ScalarAsync(scoped.ConnectionString,
                $"SELECT RealIP FROM loguserconnect WHERE id={id}"));
            Assert.Equal("198.51.100.10", await ScalarAsync(scoped.ConnectionString,
                $"SELECT userip FROM loguserconnect WHERE id={id}"));
            Assert.Equal(76, Convert.ToInt32(await ScalarAsync(scoped.ConnectionString,
                $"SELECT country FROM loguserconnect WHERE id={id}")));

            await database.UpdateConnectionRealIpAsync(id, "10.20.30.40");
            Assert.Equal("10.20.30.40", await ScalarAsync(scoped.ConnectionString,
                $"SELECT RealIP FROM loguserconnect WHERE id={id}"));

            await database.CloseConnectionLogAsync(id, 231);
            Assert.Equal(231, Convert.ToInt32(await ScalarAsync(scoped.ConnectionString,
                $"SELECT note FROM loguserconnect WHERE id={id}")));
            Assert.NotNull(await ScalarAsync(scoped.ConnectionString,
                $"SELECT disconnecttime FROM loguserconnect WHERE id={id}"));
        }
        finally
        {
            await ExecuteAsync(admin.ConnectionString, $"DROP DATABASE IF EXISTS `{databaseName}`");
        }
    }

    private static WorldConfig.DbConfig DbConfig(MySqlConnectionStringBuilder value) => new()
    {
        Ip = value.Server,
        Port = (int)value.Port,
        User = value.UserID,
        Pass = value.Password,
        Name = value.Database
    };

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        object? result = await command.ExecuteScalarAsync();
        return result is DBNull ? null : result;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
