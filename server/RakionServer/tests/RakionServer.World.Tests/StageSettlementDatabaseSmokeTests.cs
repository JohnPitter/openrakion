using System;
using System.Linq;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.World;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class StageSettlementDatabaseSmokeTests
{
    [Fact]
    public async Task SettlementIsAtomicIdempotentAndPreservesBestRank()
    {
        string? value = Environment.GetEnvironmentVariable("RAKION_MYSQL_SMOKE_CONNECTION");
        if (string.IsNullOrWhiteSpace(value)) return;
        var admin = new MySqlConnectionStringBuilder(value);
        string databaseName = "rakion_stage_smoke_" + Guid.NewGuid().ToString("N");
        var scoped = new MySqlConnectionStringBuilder(admin.ConnectionString) { Database = databaseName };
        admin.Database = "";
        await ExecuteAsync(admin.ConnectionString, $"CREATE DATABASE `{databaseName}`");
        try
        {
            await CreateFixtureAsync(scoped.ConnectionString);
            var database = new WorldDatabase(DbConfig(scoped));
            await database.EnsureStageSettlementSchemaAsync();
            Assert.Equal("InnoDB", await ReadEngineAsync(scoped.ConnectionString));

            Guid runId = Guid.NewGuid();
            StageSettlementRequest request = Request(runId, rank: 4);
            StageSettlementResult first = await database.SettleStageResultAsync(request);
            StageSettlementResult replay = await database.SettleStageResultAsync(request);
            Assert.Equal(StageSettlementStatus.Applied, first.Status);
            Assert.Equal(StageSettlementStatus.Replay, replay.Status);
            Assert.Equal("60|1|0|125|4|1|118|3", await ReadStateAsync(scoped.ConnectionString));

            StageSettlementRequest divergent = request with
            {
                Award = request.Award with { ReportedGold = 26 }
            };
            Assert.Equal(StageSettlementStatus.Failed,
                (await database.SettleStageResultAsync(divergent)).Status);
            StageSettlementRequest divergentMetric = request with
            {
                Award = request.Award with { CellExpSlot1 = 1 }
            };
            Assert.Equal(StageSettlementStatus.Failed,
                (await database.SettleStageResultAsync(divergentMetric)).Status);
            Assert.Equal("60|1|0|125|4|1|118|3", await ReadStateAsync(scoped.ConnectionString));

            Guid concurrentRun = Guid.NewGuid();
            StageSettlementRequest concurrent = Request(concurrentRun, rank: 3,
                beforeExp: 60, afterExp: 70, goldBefore: 125, goldAfter: 130,
                reportedExp: 10, reportedGold: 5, cellBeforeExp: 118, cellReportedExp: 3);
            StageSettlementResult[] results = await Task.WhenAll(
                database.SettleStageResultAsync(concurrent),
                database.SettleStageResultAsync(concurrent));
            Assert.Equal(1, results.Count(result => result.Status == StageSettlementStatus.Applied));
            Assert.Equal(1, results.Count(result => result.Status == StageSettlementStatus.Replay));
            Assert.Equal("70|1|0|130|4|2|121|6", await ReadStateAsync(scoped.ConnectionString));

            StageSettlementRequest stale = Request(Guid.NewGuid(), rank: 5,
                beforeExp: 1, afterExp: 2, goldBefore: 130, goldAfter: 131,
                reportedExp: 1, reportedGold: 1);
            Assert.Equal(StageSettlementStatus.Failed,
                (await database.SettleStageResultAsync(stale)).Status);
            Assert.Equal("70|1|0|130|4|2|121|6", await ReadStateAsync(scoped.ConnectionString));
        }
        finally
        {
            await ExecuteAsync(admin.ConnectionString, $"DROP DATABASE IF EXISTS `{databaseName}`");
        }
    }

    private static StageSettlementRequest Request(
        Guid runId, byte rank, long beforeExp = 10, long afterExp = 60,
        uint goldBefore = 100, uint goldAfter = 125,
        uint reportedExp = 50, uint reportedGold = 25,
        long cellBeforeExp = 100, uint cellReportedExp = 18)
    {
        var before = new CharacterProgressionState(beforeExp, 1, 0);
        var after = new CharacterProgressionState(afterExp, 1, 0);
        var identity = new StageRunIdentity(runId, 1, 1, 3);
        var award = new StageResultAward(
            rank, reportedExp, reportedGold, cellReportedExp, 0, 0,
            reportedExp, reportedGold);
        var cell = new EquippedCellState(10, 7, 8000, 8000007, 1, cellBeforeExp);
        var empty11 = new EquippedCellState(11, 0, 0, 0, 0, 0);
        var empty12 = new EquippedCellState(12, 0, 0, 0, 0, 0);
        return new StageSettlementRequest(identity, award, before, after, goldBefore, goldAfter)
        {
            Cells = new CellProgressionChange[]
            {
                new(cell, cell with { Exp = cellBeforeExp + cellReportedExp },
                    cellReportedExp, cellReportedExp),
                new(empty11, empty11, 0, 0),
                new(empty12, empty12, 0, 0)
            }
        };
    }

    private static async Task CreateFixtureAsync(string connection)
    {
        await ExecuteAsync(connection,
            "CREATE TABLE characterinfo(id INT PRIMARY KEY,exp BIGINT NOT NULL," +
            "level TINYINT UNSIGNED NOT NULL,levelpoint TINYINT UNSIGNED NOT NULL) ENGINE=InnoDB");
        await ExecuteAsync(connection,
            "CREATE TABLE usergameinfo(id INT PRIMARY KEY,gold INT UNSIGNED NOT NULL) ENGINE=InnoDB");
        await ExecuteAsync(connection,
            "CREATE TABLE userstageinfo(id INT AUTO_INCREMENT PRIMARY KEY,characterid INT NOT NULL," +
            "stage TINYINT UNSIGNED NOT NULL,rank INT NOT NULL,updatetime DATETIME NOT NULL) ENGINE=MyISAM");
        await ExecuteAsync(connection,
            "CREATE TABLE useriteminfo(id INT PRIMARY KEY,userid INT NOT NULL,itemid INT NOT NULL," +
            "level TINYINT UNSIGNED NOT NULL,exp BIGINT NOT NULL) ENGINE=InnoDB");
        await ExecuteAsync(connection,
            "INSERT INTO characterinfo VALUES(1,10,1,0);" +
            "INSERT INTO usergameinfo VALUES(1,100);" +
            "INSERT INTO userstageinfo(characterid,stage,rank,updatetime) VALUES(1,3,2,NOW());" +
            "INSERT INTO useriteminfo VALUES(7,1,8000,1,100)");
    }

    private static async Task<string> ReadStateAsync(string connection)
    {
        await using var c = new MySqlConnection(connection);
        await c.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT ch.exp,ch.level,ch.levelpoint,g.gold,s.rank," +
            "(SELECT COUNT(*) FROM stage_result_settlement_ledger),i.exp," +
            "(SELECT COUNT(*) FROM stage_result_cell_settlement_ledger) " +
            "FROM characterinfo ch JOIN usergameinfo g ON g.id=1 " +
            "JOIN userstageinfo s ON s.characterid=ch.id AND s.stage=3 " +
            "JOIN useriteminfo i ON i.id=7 WHERE ch.id=1", c);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return string.Join('|', Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetValue));
    }

    private static async Task<string> ReadEngineAsync(string connection)
    {
        await using var c = new MySqlConnection(connection);
        await c.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT ENGINE FROM information_schema.TABLES " +
            "WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='userstageinfo'", c);
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

    private static WorldConfig.DbConfig DbConfig(MySqlConnectionStringBuilder value) => new()
    {
        Ip = value.Server,
        Port = (int)value.Port,
        User = value.UserID,
        Pass = value.Password,
        Name = value.Database
    };

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var c = new MySqlConnection(connection);
        await c.OpenAsync();
        await using var command = new MySqlCommand(sql, c);
        await command.ExecuteNonQueryAsync();
    }
}
