using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.World;
using RakionServer.World.Database;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class MatchSettlementDatabaseSmokeTests
{
    [Fact]
    public async Task SettlementIsAtomicIdempotentAndRejectsDivergentReplay()
    {
        string? value = Environment.GetEnvironmentVariable("RAKION_MYSQL_SMOKE_CONNECTION");
        if (string.IsNullOrWhiteSpace(value)) return;

        var admin = new MySqlConnectionStringBuilder(value);
        string databaseName = "rakion_match_test_" + Guid.NewGuid().ToString("N");
        var scoped = new MySqlConnectionStringBuilder(admin.ConnectionString) { Database = databaseName };
        admin.Database = "";
        await ExecuteAsync(admin.ConnectionString, $"CREATE DATABASE `{databaseName}`");
        try
        {
            await ExecuteAsync(scoped.ConnectionString,
                "CREATE TABLE characterinfo (id INT NOT NULL PRIMARY KEY," +
                "win INT NOT NULL,lose INT NOT NULL,draw INT NOT NULL) ENGINE=InnoDB");
            await ExecuteAsync(scoped.ConnectionString,
                "INSERT INTO characterinfo VALUES (1,0,0,0),(2,0,0,0)");
            var database = new WorldDatabase(WorldDbConfig(scoped));
            await database.EnsureMatchSettlementSchemaAsync();

            Guid matchId = Guid.NewGuid();
            MatchSettlementEntry[] entries = [new(1, 1, 0, 0), new(2, 0, 1, 0)];
            Assert.True(await database.SettleMatchAsync(matchId, entries));
            Assert.True(await database.SettleMatchAsync(matchId, entries));
            Assert.Equal("1,0,0|0,1,0", await ReadResultsAsync(scoped.ConnectionString));

            Assert.False(await database.SettleMatchAsync(matchId, [new(1, 0, 0, 1)]));
            Assert.Equal("1,0,0|0,1,0", await ReadResultsAsync(scoped.ConnectionString));

            Assert.False(await database.SettleMatchAsync(Guid.NewGuid(),
                [new(1, 0, 0, 1), new(999, 0, 0, 1)]));
            Assert.Equal("1,0,0|0,1,0", await ReadResultsAsync(scoped.ConnectionString));
        }
        finally
        {
            await ExecuteAsync(admin.ConnectionString, $"DROP DATABASE IF EXISTS `{databaseName}`");
        }
    }

    private static WorldConfig.DbConfig WorldDbConfig(MySqlConnectionStringBuilder value) =>
        new() { Ip = value.Server, Port = (int)value.Port, User = value.UserID,
            Pass = value.Password, Name = value.Database };

    private static async Task<string> ReadResultsAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT GROUP_CONCAT(CONCAT(win,',',lose,',',draw) ORDER BY id SEPARATOR '|') " +
            "FROM characterinfo", connection);
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? "";
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
