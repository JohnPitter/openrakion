using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Buddy;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class BuddyFriendDatabaseSmokeTests
{
    [Fact]
    public async Task FriendAndGroupMutationsPersistAtomically()
    {
        string? value = Environment.GetEnvironmentVariable("RAKION_MYSQL_SMOKE_CONNECTION");
        if (string.IsNullOrWhiteSpace(value)) return;

        var admin = new MySqlConnectionStringBuilder(value);
        string databaseName = "rakion_buddy_" + Guid.NewGuid().ToString("N");
        var scoped = new MySqlConnectionStringBuilder(admin.ConnectionString) { Database = databaseName };
        admin.Database = "";
        await ExecuteAsync(admin.ConnectionString, $"CREATE DATABASE `{databaseName}`");
        try
        {
            await CreateFixtureAsync(scoped.ConnectionString);
            var database = new BuddyDatabase(scoped.ConnectionString);
            await database.EnsureSchemaAsync();

            Assert.Contains(await database.LoadFriendsAsync("alice"),
                friend => friend.AccountId == "bob" && friend.GroupName == "Legacy");
            byte[] extension = new byte[32];
            extension[0] = 0xA5;
            BuddyFriendRecord? added = await database.AddFriendAsync("alice", "carol", extension);
            Assert.NotNull(added);
            Assert.Equal(0xA5, added.Extension[0]);
            Assert.Contains(await database.LoadFriendsAsync("carol"),
                friend => friend.AccountId == "alice");

            var group = new BuddyGroupRecord(7, "Raid", 9);
            Assert.True(await database.AddGroupAsync("alice", group));
            Assert.True(await database.AssignGroupAsync("alice", "Raid", ["carol"]));
            Assert.True(await database.RenameGroupAsync("alice", "Raid", "Party"));
            Assert.Equal("Party", Assert.Single(
                await database.LoadFriendsAsync("alice"),
                friend => friend.AccountId == "carol").GroupName);
            await database.SaveProfileAsync("alice", "Knights", null);
            await database.SaveProfileAsync("alice", "", null);
            Assert.Equal("", await ReadGuildAsync(scoped.ConnectionString, "alice"));
            Assert.True(await database.RemoveFriendAsync("alice", "carol"));
            Assert.DoesNotContain(await database.LoadFriendsAsync("carol"),
                friend => friend.AccountId == "alice");
        }
        finally
        {
            await ExecuteAsync(admin.ConnectionString, $"DROP DATABASE IF EXISTS `{databaseName}`");
        }
    }

    private static async Task CreateFixtureAsync(string connectionString)
    {
        await ExecuteAsync(connectionString,
            "CREATE TABLE user(id VARCHAR(16) PRIMARY KEY,password VARCHAR(32) NOT NULL) ENGINE=InnoDB");
        await ExecuteAsync(connectionString,
            "CREATE TABLE usergameinfo(name VARCHAR(16) PRIMARY KEY,buddyname VARCHAR(20) NOT NULL," +
            "charname VARCHAR(20) NOT NULL) ENGINE=InnoDB");
        await ExecuteAsync(connectionString,
            "CREATE TABLE buddylist(Id CHAR(16),Category CHAR(20),Buddy CHAR(16)) ENGINE=MyISAM");
        await ExecuteAsync(connectionString,
            "INSERT INTO user VALUES('alice','x'),('bob','x'),('carol','x');" +
            "INSERT INTO usergameinfo VALUES" +
            "('alice','Alice','AliceChar'),('bob','Bob','BobChar'),('carol','Carol','CarolChar');" +
            "INSERT INTO buddylist VALUES('alice','Legacy','bob')");
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadGuildAsync(string connectionString, string accountId)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT guild_name FROM buddy_profile WHERE account_id=@account", connection);
        command.Parameters.AddWithValue("@account", accountId);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
