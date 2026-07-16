using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.World;
using RakionServer.World.Database;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class InventoryExpirationDatabaseSmokeTests
{
    [Fact]
    public async Task PurgeRemovesExpiredStorageGearAndQuickslotTogether()
    {
        string? connectionValue = Environment.GetEnvironmentVariable("RAKION_MYSQL_SMOKE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionValue)) return;

        var admin = new MySqlConnectionStringBuilder(connectionValue);
        string databaseName = "rakion_inv_exp_" + Guid.NewGuid().ToString("N");
        var scoped = new MySqlConnectionStringBuilder(admin.ConnectionString) { Database = databaseName };
        admin.Database = "";
        await ExecuteAsync(admin.ConnectionString, $"CREATE DATABASE `{databaseName}`");
        try
        {
            await CreateFixtureAsync(scoped.ConnectionString);
            var database = new WorldDatabase(DbConfig(scoped));

            int removed = await database.PurgeExpiredInventoryAsync(7);

            Assert.Equal(3, removed);
            Assert.Equal(2L, await ScalarAsync(scoped.ConnectionString,
                "SELECT COUNT(*) FROM useriteminfo WHERE userid=7"));
            Assert.Equal(0L, await ScalarAsync(scoped.ConnectionString,
                "SELECT COUNT(*) FROM useriteminfo WHERE userid=7 AND itemid IN (1001,1101,12001)"));
        }
        finally
        {
            await ExecuteAsync(admin.ConnectionString, $"DROP DATABASE IF EXISTS `{databaseName}`");
        }
    }

    private static async Task CreateFixtureAsync(string connectionString)
    {
        await ExecuteAsync(connectionString,
            "CREATE TABLE useriteminfo (id INT PRIMARY KEY AUTO_INCREMENT,userid INT NOT NULL," +
            "characterid INT NOT NULL,itemid INT NOT NULL,slot TINYINT UNSIGNED NOT NULL," +
            "limittime INT NOT NULL) ENGINE=InnoDB");
        const string marker = "((TO_DAYS(NOW())*24+HOUR(NOW()))*60+MINUTE(NOW()))";
        await ExecuteAsync(connectionString,
            "INSERT INTO useriteminfo(userid,characterid,itemid,slot,limittime) VALUES" +
            $"(7,0,1001,0,{marker}-5)," +
            $"(7,70,1101,1,{marker}-5)," +
            $"(7,70,12001,13,{marker}-5)," +
            "(7,0,2001,1,0)," +
            $"(7,70,2101,2,{marker}+5)," +
            $"(8,0,3001,0,{marker}-5)");
    }

    private static WorldConfig.DbConfig DbConfig(MySqlConnectionStringBuilder value) => new()
    {
        Ip = value.Server,
        Port = (int)value.Port,
        User = value.UserID,
        Pass = value.Password,
        Name = value.Database
    };

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
