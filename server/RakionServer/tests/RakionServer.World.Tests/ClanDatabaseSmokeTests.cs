using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.World;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class ClanDatabaseSmokeTests
{
    [Fact]
    public async Task LoginSnapshotLoadsClanMasterAndBoundedTree()
    {
        string? value = Environment.GetEnvironmentVariable("RAKION_MYSQL_SMOKE_CONNECTION");
        if (string.IsNullOrWhiteSpace(value)) return;

        var admin = new MySqlConnectionStringBuilder(value);
        string databaseName = "rakion_clan_" + Guid.NewGuid().ToString("N");
        var scoped = new MySqlConnectionStringBuilder(admin.ConnectionString) { Database = databaseName };
        admin.Database = "";
        await ExecuteAsync(admin.ConnectionString, $"CREATE DATABASE `{databaseName}`");
        try
        {
            await CreateFixtureAsync(scoped.ConnectionString);
            var config = new WorldConfig.DbConfig
            {
                Ip = scoped.Server,
                Port = checked((int)scoped.Port),
                User = scoped.UserID,
                Pass = scoped.Password,
                Name = databaseName
            };
            var database = new WorldDatabase(config);

            ClanLoginSnapshot snapshot = await database.LoadClanLoginSnapshotAsync(1, 7);

            Assert.Equal("ProbeClan", snapshot.Name);
            Assert.Equal("MasterZZ", snapshot.MasterCharacterName);
            Assert.Equal("parent", snapshot.TreeUpperAccount);
            Assert.Equal("ParentChar", snapshot.TreeUpperCharacter);
            Assert.Equal(7, snapshot.Children.Count);
            Assert.DoesNotContain(snapshot.Children, child => child.AccountName == "kid8");
            Assert.Same(ClanLoginSnapshot.Empty,
                await database.LoadClanLoginSnapshotAsync(1, 0));
        }
        finally
        {
            await ExecuteAsync(admin.ConnectionString, $"DROP DATABASE IF EXISTS `{databaseName}`");
        }
    }

    private static async Task CreateFixtureAsync(string connectionString)
    {
        await ExecuteAsync(connectionString,
            "CREATE TABLE claninfo(id INT PRIMARY KEY,masterid INT,name VARCHAR(12)," +
            "point INT,members SMALLINT,rank INT UNSIGNED);" +
            "CREATE TABLE usergameinfo(id INT PRIMARY KEY,name VARCHAR(16),charname VARCHAR(20)," +
            "clanid INT,clanpoint INT,clanrank INT,treeuppername VARCHAR(16),treerank INT);" +
            "INSERT INTO claninfo VALUES(7,2,'ProbeClan',100,9,3);" +
            "INSERT INTO usergameinfo VALUES" +
            "(1,'alice','Alice',7,50,4,'parent',5)," +
            "(2,'master','MasterZZ',7,0,0,'',0)," +
            "(3,'parent','ParentChar',7,0,0,'',0)," + ChildrenSql());
    }

    private static string ChildrenSql()
    {
        var rows = new string[8];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = $"({10 + i},'kid{i + 1}','Kid{i + 1}',7,0,0,'alice',0)";
        return string.Join(',', rows);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
