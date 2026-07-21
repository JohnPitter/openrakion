using System;
using System.IO;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;
using RakionServer.LauncherWeb;
using RakionServer.World.Database;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class LauncherTicketDatabaseSmokeTests
{
    [Fact]
    public async Task TicketIsAccountBoundSingleUseAndExpires()
    {
        string? connectionValue = Environment.GetEnvironmentVariable(
            "RAKION_MYSQL_SMOKE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionValue)) return;

        var admin = new MySqlConnectionStringBuilder(connectionValue);
        string database = "rakion_ticket_test_" + Guid.NewGuid().ToString("N");
        var scoped = new MySqlConnectionStringBuilder(admin.ConnectionString)
        {
            Database = database
        };
        admin.Database = "";

        await ExecuteAsync(admin.ConnectionString, $"CREATE DATABASE `{database}`");
        try
        {
            await ExecuteAsync(scoped.ConnectionString,
                "CREATE TABLE user (id VARCHAR(16) NOT NULL PRIMARY KEY," +
                "password VARCHAR(128) NOT NULL,Authority INT NOT NULL,country INT NOT NULL) ENGINE=InnoDB");
            await ExecuteAsync(scoped.ConnectionString,
                "INSERT INTO user VALUES ('test','secret',2,76),('other','other',0,0)");
            await ExecuteAsync(scoped.ConnectionString,
                "CREATE TABLE launcher_ticket (token_hash BINARY(32) NOT NULL PRIMARY KEY," +
                "account_id VARCHAR(16) NOT NULL,expires_at DATETIME(6) NOT NULL," +
                "used_at DATETIME(6) NULL,created_at DATETIME(6) NOT NULL) ENGINE=InnoDB");

            var webConfig = new LauncherWebConfig(new Uri("http://127.0.0.1/"),
                false, false, true, true, 60, scoped.ConnectionString, ".", null);
            var issuer = new LauncherTicketRepository(
                webConfig, new ActiveAccountLookup(Path.Combine(database, "active.json")));
            await issuer.EnsureSchemaAsync();
            var world = new WorldDatabase(WorldDbConfig(scoped));
            var build = new LauncherBuildIdentity(11001, 259);

            LauncherTicketIssueResult issuedResult =
                await issuer.IssueAsync("test", "secret", build, default);
            Assert.Equal(LauncherTicketIssueStatus.Success, issuedResult.Status);
            IssuedLauncherTicket issued = Assert.IsType<IssuedLauncherTicket>(issuedResult.Ticket);
            Assert.Null(await world.AuthenticateCredentialAsync(
                "other", issued.Ticket, allowPasswordLogin: false));
            Assert.Null(await world.AuthenticateCredentialAsync(
                "test", issued.Ticket, allowPasswordLogin: false,
                new LauncherBuildIdentity(11001, 258)));
            WorldDatabase.Account account = Assert.IsType<WorldDatabase.Account>(
                await world.AuthenticateCredentialAsync(
                    "test", issued.Ticket, allowPasswordLogin: false, build));
            Assert.Equal(2, account.Authority);
            Assert.Null(await world.AuthenticateCredentialAsync(
                "test", issued.Ticket, allowPasswordLogin: false));

            LauncherTicketIssueResult expiredResult =
                await issuer.IssueAsync("test", "secret", build, default);
            IssuedLauncherTicket expired = Assert.IsType<IssuedLauncherTicket>(expiredResult.Ticket);
            await ExecuteAsync(scoped.ConnectionString,
                "UPDATE launcher_ticket SET expires_at=UTC_TIMESTAMP(6)-INTERVAL 1 SECOND " +
                "WHERE used_at IS NULL");
            Assert.Null(await world.AuthenticateCredentialAsync(
                "test", expired.Ticket, allowPasswordLogin: false));
        }
        finally
        {
            await ExecuteAsync(admin.ConnectionString, $"DROP DATABASE IF EXISTS `{database}`");
        }
    }

    private static WorldConfig.DbConfig WorldDbConfig(MySqlConnectionStringBuilder value) =>
        new()
        {
            Ip = value.Server,
            Port = (int)value.Port,
            User = value.UserID,
            Pass = value.Password,
            Name = value.Database
        };

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
