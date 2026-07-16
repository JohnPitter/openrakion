using System;
using System.Text;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class GamePointSettlementDatabaseSmokeTests
{
    [Fact]
    public async Task GamePointIsAtomicIdempotentAndRejectsDivergentReplay()
    {
        string? value = Environment.GetEnvironmentVariable("RAKION_MYSQL_SMOKE_CONNECTION");
        if (string.IsNullOrWhiteSpace(value)) return;

        var admin = new MySqlConnectionStringBuilder(value);
        string databaseName = "rakion_point_test_" + Guid.NewGuid().ToString("N");
        var scoped = new MySqlConnectionStringBuilder(admin.ConnectionString) { Database = databaseName };
        admin.Database = "";
        await ExecuteAsync(admin.ConnectionString, $"CREATE DATABASE `{databaseName}`");
        try
        {
            await CreateFixtureAsync(scoped.ConnectionString);
            var database = new WorldDatabase(WorldDbConfig(scoped));
            await database.EnsureGamePointSettlementSchemaAsync();
            var curve = await database.LoadCellLevelCurveAsync();
            Assert.Equal(35, curve[(0, 2)]);

            Guid matchId = Guid.NewGuid();
            var before = new CharacterProgressionState(10, 1, 0);
            var after = new CharacterProgressionState(50, 2, 3);
            var identity = new GamePointIdentity(matchId, 1, 1, 1);
            CellProgressionChange[] cells = FirstCellChanges();
            var request = new GamePointSettlementRequest(
                identity, new GamePointAward(40, 25), before, after) { Cells = cells };

            Assert.Equal(GamePointSettlementStatus.Applied,
                (await database.SettleGamePointAsync(request)).Status);
            Assert.Equal(GamePointSettlementStatus.Replay,
                (await database.SettleGamePointAsync(request)).Status);
            Assert.Equal("50,2,3|125|10:3:100,12:99:1000|1|3",
                await ReadStateAsync(scoped.ConnectionString));

            CellProgressionChange[] divergentCells = FirstCellChanges();
            divergentCells[0] = divergentCells[0] with { ReportedExp = 499 };
            var divergent = request with { Cells = divergentCells };
            Assert.Equal(GamePointSettlementStatus.Failed,
                (await database.SettleGamePointAsync(divergent)).Status);
            Assert.Equal("50,2,3|125|10:3:100,12:99:1000|1|3",
                await ReadStateAsync(scoped.ConnectionString));

            var concurrent = new GamePointSettlementRequest(
                identity with { Round = 2 }, new GamePointAward(10, 5), after,
                new CharacterProgressionState(60, 2, 3)) { Cells = SecondCellChanges() };
            GamePointSettlementResult[] concurrentResults = await Task.WhenAll(
                database.SettleGamePointAsync(concurrent),
                database.SettleGamePointAsync(concurrent));
            Assert.Contains(concurrentResults,
                result => result.Status == GamePointSettlementStatus.Applied);
            Assert.Contains(concurrentResults,
                result => result.Status == GamePointSettlementStatus.Replay);
            Assert.Equal("60,2,3|130|10:3:110,12:99:1000|2|6",
                await ReadStateAsync(scoped.ConnectionString));

            var stale = request with {
                Identity = identity with { Round = 3 },
                Award = new GamePointAward(5, 5),
                After = new CharacterProgressionState(15, 1, 0)
            };
            Assert.Equal(GamePointSettlementStatus.Failed,
                (await database.SettleGamePointAsync(stale)).Status);
            Assert.Equal("60,2,3|130|10:3:110,12:99:1000|2|6",
                await ReadStateAsync(scoped.ConnectionString));
        }
        finally
        {
            await ExecuteAsync(admin.ConnectionString, $"DROP DATABASE IF EXISTS `{databaseName}`");
        }
    }

    private static async Task CreateFixtureAsync(string connectionString)
    {
        await ExecuteAsync(connectionString,
            "CREATE TABLE characterinfo (id INT NOT NULL PRIMARY KEY,userid INT NOT NULL," +
            "exp BIGINT NOT NULL,level TINYINT UNSIGNED NOT NULL," +
            "levelpoint TINYINT UNSIGNED NOT NULL) ENGINE=InnoDB");
        await ExecuteAsync(connectionString,
            "CREATE TABLE usergameinfo (id INT NOT NULL PRIMARY KEY,gold BIGINT NOT NULL) ENGINE=InnoDB");
        await ExecuteAsync(connectionString,
            "CREATE TABLE useriteminfo (id INT NOT NULL PRIMARY KEY,userid INT NOT NULL," +
            "itemid INT NOT NULL,level TINYINT UNSIGNED NOT NULL,exp BIGINT NOT NULL) ENGINE=InnoDB");
        await ExecuteAsync(connectionString,
            "CREATE TABLE npcinfo (npc INT NOT NULL,level INT NOT NULL,exp BIGINT NOT NULL) ENGINE=InnoDB");
        await ExecuteAsync(connectionString,
            "INSERT INTO characterinfo VALUES (1,1,10,1,0)");
        await ExecuteAsync(connectionString,
            "INSERT INTO usergameinfo VALUES (1,100)");
        await ExecuteAsync(connectionString,
            "INSERT INTO useriteminfo VALUES (10,1,8000,1,0),(12,1,8001,99,990)");
        await ExecuteAsync(connectionString,
            "INSERT INTO npcinfo VALUES (0,1,0),(0,2,35),(0,99,1000)");
    }

    private static WorldConfig.DbConfig WorldDbConfig(MySqlConnectionStringBuilder value) =>
        new() { Ip = value.Server, Port = (int)value.Port, User = value.UserID,
            Pass = value.Password, Name = value.Database };

    private static async Task<string> ReadStateAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT CONCAT(c.exp,',',c.level,',',c.levelpoint,'|',u.gold,'|'," +
            "(SELECT GROUP_CONCAT(CONCAT(id,':',level,':',exp) ORDER BY id) FROM useriteminfo),'|'," +
            "(SELECT COUNT(*) FROM game_point_settlement_ledger),'|'," +
            "(SELECT COUNT(*) FROM game_point_cell_settlement_ledger)) " +
            "FROM characterinfo c JOIN usergameinfo u ON u.id=c.userid", connection);
        object? value = await command.ExecuteScalarAsync();
        return value is byte[] bytes ? Encoding.ASCII.GetString(bytes) : Convert.ToString(value) ?? "";
    }

    private static CellProgressionChange[] FirstCellChanges() =>
    [
        Change(10, 10, 8000, 1, 0, 3, 100, 500, 100),
        Change(11, 0, 0, 0, 0, 0, 0, 70, 0),
        Change(12, 12, 8001, 99, 990, 99, 1000, 100, 100)
    ];

    private static CellProgressionChange[] SecondCellChanges() =>
    [
        Change(10, 10, 8000, 3, 100, 3, 110, 10, 10),
        Change(11, 0, 0, 0, 0, 0, 0, 0, 0),
        Change(12, 12, 8001, 99, 1000, 99, 1000, 10, 10)
    ];

    private static CellProgressionChange Change(
        byte slot, int row, int item, byte level0, long exp0,
        byte level1, long exp1, uint reported, uint applied)
    {
        var before = new EquippedCellState(slot, row, item, (uint)Math.Max(0, row), level0, exp0);
        var after = before with { Level = level1, Exp = exp1 };
        return new CellProgressionChange(before, after, reported, applied);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
