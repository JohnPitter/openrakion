using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.World;
using RakionServer.World.Database;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class CharacterDeleteDatabaseSmokeTests
{
    [Fact]
    public async Task HardDeleteAndProtectedSoftDeleteArePersisted()
    {
        string? connectionValue = Environment.GetEnvironmentVariable("RAKION_MYSQL_SMOKE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionValue)) return;

        var admin = new MySqlConnectionStringBuilder(connectionValue);
        string databaseName = "rakion_character_delete_test_" + Guid.NewGuid().ToString("N");
        var scoped = new MySqlConnectionStringBuilder(admin.ConnectionString) { Database = databaseName };
        admin.Database = "";
        await ExecuteAsync(admin.ConnectionString, $"CREATE DATABASE `{databaseName}`");
        try
        {
            await CreateSchemaAsync(scoped.ConnectionString);
            var config = DbConfig(scoped);
            var database = new WorldDatabase(config, config);

            CharacterDeleteOutcome hardDelete = await database.DeleteCharacterAsync(1, 10, "");
            Assert.Equal(CharacterDeleteResult.Success, hardDelete.Result);
            Assert.Equal(0L, await ScalarAsync(scoped.ConnectionString,
                "SELECT COUNT(*) FROM characterinfo WHERE id=10"));

            CharacterDeleteOutcome issued = await database.DeleteCharacterAsync(1, 20, "");
            Assert.Equal(CharacterDeleteResult.DeleteKeySent, issued.Result);
            Assert.Equal(10, issued.DeleteKey.Length);
            Assert.Equal("player@example.test", issued.Email);

            Assert.True(await database.RevokeCharacterDeleteKeyAsync(1, 20, issued.DeleteKey));
            CharacterDeleteOutcome retried = await database.DeleteCharacterAsync(1, 20, "");
            Assert.Equal(CharacterDeleteResult.DeleteKeySent, retried.Result);
            Assert.Equal(10, retried.DeleteKey.Length);

            CharacterDeleteOutcome softDelete = await database.DeleteCharacterAsync(
                1, 20, retried.DeleteKey);
            Assert.Equal(CharacterDeleteResult.Success, softDelete.Result);
            Assert.Equal(10L, await ScalarAsync(scoped.ConnectionString,
                "SELECT auth FROM characterinfo WHERE id=20"));
            Assert.Equal(2L, await ScalarAsync(scoped.ConnectionString,
                "SELECT COUNT(*) FROM logdeletecharacter"));
        }
        finally
        {
            await ExecuteAsync(admin.ConnectionString, $"DROP DATABASE IF EXISTS `{databaseName}`");
        }
    }

    private static async Task CreateSchemaAsync(string connectionString)
    {
        string[] commands =
        {
            "CREATE TABLE user (id VARCHAR(11) PRIMARY KEY,e_mail VARCHAR(50) NOT NULL) ENGINE=InnoDB",
            "CREATE TABLE usergameinfo (id INT PRIMARY KEY,name VARCHAR(11) NOT NULL," +
                "charname VARCHAR(11) NOT NULL) ENGINE=InnoDB",
            "CREATE TABLE characterinfo (id INT PRIMARY KEY,userid INT NOT NULL,name VARCHAR(11) NOT NULL," +
                "level TINYINT UNSIGNED NOT NULL,used TINYINT NOT NULL,deletekey VARCHAR(50) NOT NULL," +
                "auth TINYINT NOT NULL,createtime DATETIME NOT NULL,changetime DATETIME NOT NULL) ENGINE=InnoDB",
            "CREATE TABLE useriteminfo (id INT PRIMARY KEY AUTO_INCREMENT,characterid INT NOT NULL) ENGINE=InnoDB",
            "CREATE TABLE userstageinfo (id INT PRIMARY KEY AUTO_INCREMENT,characterid INT NOT NULL) ENGINE=InnoDB",
            "CREATE TABLE logdeletecharacter (id BIGINT PRIMARY KEY AUTO_INCREMENT,userid INT NOT NULL," +
                "charname VARCHAR(11) NOT NULL,deletetime DATETIME(6) NOT NULL," +
                "level TINYINT UNSIGNED NOT NULL,mode TINYINT UNSIGNED NOT NULL) ENGINE=InnoDB",
            "INSERT INTO user VALUES ('account','player@example.test')",
            "INSERT INTO usergameinfo VALUES (1,'account','Selected')",
            "INSERT INTO characterinfo VALUES " +
                "(10,1,'LowHero',14,0,'legacy',0,NOW()-INTERVAL 30 DAY,'2000-01-01')," +
                "(20,1,'HighHero',15,0,'legacy',0,NOW()-INTERVAL 30 DAY,'2000-01-01')",
            "INSERT INTO useriteminfo(characterid) VALUES (10),(20)",
            "INSERT INTO userstageinfo(characterid) VALUES (10),(20)"
        };
        foreach (string command in commands)
            await ExecuteAsync(connectionString, command);
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
