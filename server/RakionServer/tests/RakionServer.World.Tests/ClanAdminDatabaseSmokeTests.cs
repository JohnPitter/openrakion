using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Admin;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class ClanAdminDatabaseSmokeTests
{
    [Fact]
    public async Task ClanLifecycleIsTransactionalAndAudited()
    {
        string? value = Environment.GetEnvironmentVariable("RAKION_MYSQL_SMOKE_CONNECTION");
        if (string.IsNullOrWhiteSpace(value)) return;

        var admin = new MySqlConnectionStringBuilder(value);
        string databaseName = "rakion_clan_admin_" + Guid.NewGuid().ToString("N");
        var scoped = new MySqlConnectionStringBuilder(admin.ConnectionString) { Database = databaseName };
        admin.Database = "";
        await ExecuteAsync(admin.ConnectionString, $"CREATE DATABASE `{databaseName}`");
        try
        {
            await CreateFixtureAsync(scoped.ConnectionString);
            var database = new AdminDb(
                scoped.ConnectionString, new AdminIdentity("smoke", AdminRole.Owner));
            await database.EnsureAuditSchemaAsync();

            int clanId = await database.CreateClanAsync("alice", "Probe Clan", "smoke create");
            await database.AddClanMemberAsync(clanId, "bob", "smoke add bob");
            await database.AddClanMemberAsync(clanId, "carol", "smoke add carol");
            await database.SetClanTreeParentAsync(
                clanId, "bob", "alice", "smoke tree");
            await FillTreeToCapacityAsync(database, clanId);
            await database.SetClanTreeParentAsync(
                clanId, "bob", "alice", "smoke idempotent tree");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.SetClanTreeParentAsync(clanId, "alice", "bob", "smoke cycle"));
            await database.TransferClanMasterAsync(clanId, "bob", "smoke transfer");
            await database.RemoveClanMemberAsync(clanId, "alice", "smoke remove");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.RemoveClanMemberAsync(clanId, "bob", "smoke master guard"));
            await database.DissolveClanAsync(clanId, "smoke dissolve");

            Assert.Equal(0L, await ScalarAsync(scoped.ConnectionString,
                "SELECT COUNT(*) FROM claninfo"));
            Assert.Equal(0L, await ScalarAsync(scoped.ConnectionString,
                "SELECT COUNT(*) FROM usergameinfo WHERE clanid<>0"));
            Assert.Equal(20L, await ScalarAsync(scoped.ConnectionString,
                "SELECT COUNT(*) FROM admin_audit"));
        }
        finally
        {
            await ExecuteAsync(admin.ConnectionString, $"DROP DATABASE IF EXISTS `{databaseName}`");
        }
    }

    [Fact]
    public void ClanNamesUseTheLegacyTwelveByteBoundary()
    {
        Assert.Equal("Probe Clan", ClanPolicy.NormalizeName(" Probe Clan "));
        Assert.Throws<ArgumentException>(() => ClanPolicy.NormalizeName(new string('x', 13)));
        Assert.Throws<ArgumentException>(() => ClanPolicy.NormalizeName("clã"));
    }

    [Fact]
    public void AccountIdentifiersUseTheLegacySixteenByteBoundary()
    {
        Assert.Equal("alice", ClanPolicy.NormalizeAccount(" alice "));
        Assert.Equal("", ClanPolicy.NormalizeOptionalAccount("  "));
        Assert.Throws<ArgumentException>(() =>
            ClanPolicy.NormalizeAccount(new string('x', 17)));
        Assert.Throws<ArgumentException>(() => ClanPolicy.NormalizeAccount("two words"));
        Assert.Throws<ArgumentException>(() => ClanPolicy.NormalizeAccount("josé"));
    }

    private static async Task FillTreeToCapacityAsync(AdminDb database, int clanId)
    {
        foreach (string account in new[] { "dave", "erin", "frank", "grace", "heidi", "ivan" })
        {
            await database.AddClanMemberAsync(clanId, account, $"smoke add {account}");
            await database.SetClanTreeParentAsync(
                clanId, account, "alice", $"smoke tree {account}");
        }
    }

    private static async Task CreateFixtureAsync(string connectionString)
    {
        await ExecuteAsync(connectionString,
            "CREATE TABLE claninfo(id INT NOT NULL AUTO_INCREMENT PRIMARY KEY," +
            "masterid INT NOT NULL,mastername VARCHAR(16),name VARCHAR(12),point INT," +
            "members SMALLINT,rank INT UNSIGNED,createtime DATETIME,country SMALLINT," +
            "UNIQUE KEY ux_claninfo_name(name)) ENGINE=InnoDB;" +
            "CREATE TABLE usergameinfo(id INT PRIMARY KEY,name VARCHAR(16),charname VARCHAR(20)," +
            "clanid INT,clanpoint INT,clanrank INT,clangrade INT,treeuppername VARCHAR(16)," +
            "treerank INT,country INT,INDEX ix_clan(clanid),INDEX ix_tree(treeuppername)) ENGINE=InnoDB;" +
            "INSERT INTO usergameinfo VALUES" +
            "(1,'alice','Alice',0,0,0,0,'',0,1)," +
            "(2,'bob','Bob',0,0,0,0,'',0,1)," +
            "(3,'carol','Carol',0,0,0,0,'',0,1)," +
            "(4,'dave','Dave',0,0,0,0,'',0,1)," +
            "(5,'erin','Erin',0,0,0,0,'',0,1)," +
            "(6,'frank','Frank',0,0,0,0,'',0,1)," +
            "(7,'grace','Grace',0,0,0,0,'',0,1)," +
            "(8,'heidi','Heidi',0,0,0,0,'',0,1)," +
            "(9,'ivan','Ivan',0,0,0,0,'',0,1)");
    }

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
