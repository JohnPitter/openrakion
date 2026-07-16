using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.World;
using RakionServer.World.Database;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class PresentDatabaseSmokeTests
{
    [Fact]
    public async Task AcceptAndDisposeAreAtomicFifoAndReplaySafe()
    {
        string? value = Environment.GetEnvironmentVariable("RAKION_MYSQL_SMOKE_CONNECTION");
        if (string.IsNullOrWhiteSpace(value)) return;

        var admin = new MySqlConnectionStringBuilder(value);
        string databaseName = "rakion_present_" + Guid.NewGuid().ToString("N");
        var scoped = new MySqlConnectionStringBuilder(admin.ConnectionString) { Database = databaseName };
        admin.Database = "";
        await ExecuteAsync(admin.ConnectionString, $"CREATE DATABASE `{databaseName}`");
        try
        {
            await CreateFixtureAsync(scoped.ConnectionString);
            var database = new WorldDatabase(DbConfig(scoped));

            PresentAcceptResult[] concurrent = await Task.WhenAll(
                database.AcceptPresentAsync(7, 1, 37, true),
                database.AcceptPresentAsync(7, 1, 38, true));
            PresentAcceptResult accepted = Assert.Single(
                concurrent, result => result.Status == PresentAcceptStatus.Success);
            Assert.Single(concurrent, result => result.Status == PresentAcceptStatus.Empty);
            Assert.Equal(accepted.Slot, await ScalarAsync(scoped.ConnectionString,
                "SELECT slot FROM useriteminfo WHERE userid=7 AND itemid=1040"));
            Assert.Equal(2L, await ScalarAsync(scoped.ConnectionString,
                "SELECT COUNT(*) FROM useriteminfo WHERE userid=7"));

            int[] activeItems = new int[19], activeRows = new int[19];
            int[] staleBox = new int[120], staleRows = new int[120];
            staleBox[accepted.Slot] = 1050;
            staleRows[accepted.Slot] = 1;
            Assert.False(await database.SaveInventoryLayoutAsync(
                7, 70, activeItems, activeRows, staleBox, staleRows));
            Assert.Equal(0L, await ScalarAsync(scoped.ConnectionString,
                "SELECT slot FROM useriteminfo WHERE id=1"));

            await AddPresentAsync(scoped.ConnectionString, 2);
            PresentAcceptResult occupied = await database.AcceptPresentAsync(
                7, 2, accepted.Slot, true);
            Assert.Equal(PresentAcceptStatus.SlotOccupied, occupied.Status);
            Assert.Equal(1L, await ScalarAsync(scoped.ConnectionString,
                "SELECT COUNT(*) FROM pendingpresents WHERE id=2"));

            Assert.Equal(PresentDisposeStatus.Success,
                (await database.DisposePresentAsync(7, 2)).Status);
            Assert.Equal(PresentDisposeStatus.Empty,
                (await database.DisposePresentAsync(7, 2)).Status);
            Assert.Equal(1L, await ScalarAsync(scoped.ConnectionString,
                "SELECT COUNT(*) FROM logpresent WHERE pending_id=2 AND dispose_time IS NOT NULL"));
        }
        finally
        {
            await ExecuteAsync(admin.ConnectionString, $"DROP DATABASE IF EXISTS `{databaseName}`");
        }
    }

    private static async Task CreateFixtureAsync(string connectionString)
    {
        await ExecuteAsync(connectionString,
            "CREATE TABLE iteminfo(id INT PRIMARY KEY,type INT NOT NULL) ENGINE=InnoDB");
        await ExecuteAsync(connectionString,
            "CREATE TABLE useriteminfo(id INT PRIMARY KEY AUTO_INCREMENT,userid INT NOT NULL," +
            "characterid INT NOT NULL,itemid INT NOT NULL,item_sn BIGINT NOT NULL," +
            "sn_type INT NOT NULL,level INT NOT NULL,limittime INT NOT NULL," +
            "slot INT NOT NULL,exp BIGINT NOT NULL) ENGINE=InnoDB");
        await ExecuteAsync(connectionString,
            "CREATE TABLE pendingpresents(id INT PRIMARY KEY,present_id INT NOT NULL," +
            "user_id INT NOT NULL,added_time DATETIME NOT NULL) ENGINE=InnoDB");
        await ExecuteAsync(connectionString,
            "CREATE TABLE logpresent(pending_id INT PRIMARY KEY,present_id INT NOT NULL," +
            "sender_id INT NOT NULL,user_id INT NOT NULL,present_time DATETIME NOT NULL," +
            "dispose_time DATETIME NULL,accept_time DATETIME NULL) ENGINE=InnoDB");
        await ExecuteAsync(connectionString,
            "INSERT INTO iteminfo(id,type) VALUES(1040,0),(1050,0)");
        await ExecuteAsync(connectionString,
            "INSERT INTO useriteminfo(userid,characterid,itemid,item_sn,sn_type,level," +
            "limittime,slot,exp) VALUES(7,0,1050,8000001,3,0,0,0,0)");
        await AddPresentAsync(connectionString, 1);
    }

    private static Task AddPresentAsync(string connectionString, int id) => ExecuteAsync(
        connectionString,
        $"INSERT INTO pendingpresents VALUES({id},1040,7,NOW());" +
        $"INSERT INTO logpresent VALUES({id},1040,0,7,NOW(),NULL,NULL)");

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
