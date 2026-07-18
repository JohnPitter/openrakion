using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>Valida clear e settlement PvE pelo socket real, inclusive replay idempotente.</summary>
    [Collection("E2E")]
    public sealed class SoloStageSettlementE2ETests
    {
        [Fact]
        public async Task ExactReward_AppliesOnceAndIdenticalReplayOnlyAcknowledges()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            StageFixtureSnapshot baseline = await ReadSnapshotAsync(fixture.DbConnectionString);

            await using var player = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "stage-settlement");
            ClientSession session = DriveToStageOne(fixture.Server!, player);
            Guid runId = session.StageRunId;
            IReadOnlyList<EquippedCellState> cells = session.GetEquippedCellStates();

            try
            {
                StageReward reward = fixture.Server!.CalculateStageReward(
                    1, 5, session.StageRunPreviousBestRank)
                    ?? throw new InvalidOperationException("Reward do stage 1 ausente.");
                IReadOnlyList<uint> cellExp = ExpectedCellExp(session, reward.Exp);
                var result = new HeadlessWorldClient.StageResultSpec(
                    1, 5, new HeadlessWorldClient.StageRewardPayload(reward.Exp, reward.Gold), cellExp);
                var expected = new ExpectedProgression(
                    session.CharExp + session.BonusExp(reward.Exp),
                    session.Gold + session.BonusGold(reward.Gold));

                player.ClearStage();
                player.WaitFor(IsStageClearFrame, JourneyHelper.Timeout);
                JourneyHelper.WaitUntil(() => session.StageRunCleared, "clear não marcou a execução");

                player.SendStageResult(result);
                player.WaitFor(IsStageResultAck, JourneyHelper.Timeout);
                await AssertPersistedAsync(fixture.DbConnectionString, session, runId, expected);

                player.SendStageResult(result);
                player.WaitForNext(IsStageResultAck, JourneyHelper.Timeout);
                await AssertPersistedAsync(fixture.DbConnectionString, session, runId, expected);
            }
            finally
            {
                await RestoreSnapshotAsync(fixture.DbConnectionString, baseline, cells, runId);
            }
        }

        private static ClientSession DriveToStageOne(WorldServer server, HeadlessWorldClient player)
        {
            player.Login("test2", "test2");
            player.WaitForFirstByte(0x0c, JourneyHelper.Timeout);
            player.SelectCharacter(9001);
            ClientSession session = JourneyHelper.WaitForSession(server, "test2",
                value => value.ActiveCharId > 0 && value.Status == UserStatus.FieldLobby);
            player.CreateRoom(new HeadlessWorldClient.RoomSpec(
                "e2e-pve-settle", 1, 0, 1, 432, 0, 1, 99));
            JourneyHelper.WaitUntil(
                () => session.FieldId >= 0 && server.GetField(session.FieldId) != null,
                "sala solo não criada");
            player.StartMatch();
            JourneyHelper.WaitUntil(() => session.Status == UserStatus.InField, "não entrou no field");
            player.SpawnField();
            JourneyHelper.WaitUntil(() => session.StageRunId != Guid.Empty, "run não iniciou");
            return session;
        }

        private static IReadOnlyList<uint> ExpectedCellExp(ClientSession session, uint stageExp)
        {
            IReadOnlyList<EquippedCellState> equipped = session.GetEquippedCellStates();
            var result = new uint[3];
            for (int index = 0; index < result.Length; index++)
            {
                bool active = equipped[index].RowId > 0 && equipped[index].ItemId is >= 8000 and < 9000;
                result[index] = StageRewardPolicy.CellExp(stageExp, active, session.ExpBonusActive);
            }
            return result;
        }

        private static async Task AssertPersistedAsync(
            string connectionString, ClientSession session, Guid runId, ExpectedProgression expected)
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            Assert.Equal(expected.Exp, await ScalarAsync<long>(connection,
                "SELECT exp FROM characterinfo WHERE id=@id", session.ActiveCharId));
            Assert.Equal(expected.Gold, await ScalarAsync<uint>(connection,
                "SELECT gold FROM usergameinfo WHERE id=@id", session.GameInfoId));
            Assert.Equal((byte)5, await ScalarAsync<byte>(connection,
                "SELECT `rank` FROM userstageinfo WHERE characterid=@id AND stage=1", session.ActiveCharId));
            Assert.Equal(1L, await ScalarAsync<long>(connection,
                "SELECT COUNT(*) FROM stage_result_settlement_ledger WHERE run_id=@id", runId.ToString("N")));
        }

        private static async Task<T> ScalarAsync<T>(MySqlConnection connection, string sql, object id)
        {
            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@id", id);
            object? value = await command.ExecuteScalarAsync();
            return (T)Convert.ChangeType(value!, typeof(T));
        }

        private static async Task<StageFixtureSnapshot> ReadSnapshotAsync(string connectionString)
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "SELECT c.exp,c.level,c.levelpoint,g.gold," +
                "(SELECT `rank` FROM userstageinfo WHERE characterid=9001 AND stage=1) " +
                "FROM characterinfo c JOIN usergameinfo g ON g.id=c.userid WHERE c.id=9001", connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), "fixture stage test2/9001 ausente");
            return new StageFixtureSnapshot(reader.GetInt64(0), reader.GetByte(1), reader.GetByte(2),
                Convert.ToUInt32(reader.GetValue(3)), reader.IsDBNull(4) ? null : reader.GetByte(4));
        }

        private static async Task RestoreSnapshotAsync(
            string connectionString, StageFixtureSnapshot snapshot,
            IReadOnlyList<EquippedCellState> cells, Guid runId)
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await RestoreProgressionAsync(connection, transaction, snapshot);
            await RestoreRankAsync(connection, transaction, snapshot.Rank);
            await RestoreCellsAsync(connection, transaction, cells);
            await DeleteStageLedgerAsync(connection, transaction, runId);
            await transaction.CommitAsync();
        }

        private static async Task RestoreProgressionAsync(
            MySqlConnection connection, MySqlTransaction transaction, StageFixtureSnapshot snapshot)
        {
            await using var command = new MySqlCommand(
                "UPDATE characterinfo c JOIN usergameinfo g ON g.id=c.userid " +
                "SET c.exp=@exp,c.level=@level,c.levelpoint=@point,g.gold=@gold WHERE c.id=9001",
                connection, transaction);
            command.Parameters.AddWithValue("@exp", snapshot.Exp);
            command.Parameters.AddWithValue("@level", snapshot.Level);
            command.Parameters.AddWithValue("@point", snapshot.LevelPoint);
            command.Parameters.AddWithValue("@gold", snapshot.Gold);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task RestoreRankAsync(
            MySqlConnection connection, MySqlTransaction transaction, byte? rank)
        {
            string sql = rank.HasValue
                ? "INSERT INTO userstageinfo(characterid,stage,`rank`,updatetime) VALUES(9001,1,@rank,NOW()) " +
                  "ON DUPLICATE KEY UPDATE `rank`=VALUES(`rank`),updatetime=NOW()"
                : "DELETE FROM userstageinfo WHERE characterid=9001 AND stage=1";
            await using var command = new MySqlCommand(sql, connection, transaction);
            if (rank.HasValue) command.Parameters.AddWithValue("@rank", rank.Value);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task RestoreCellsAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            IReadOnlyList<EquippedCellState> cells)
        {
            foreach (EquippedCellState cell in cells)
            {
                if (cell.RowId <= 0) continue;
                await using var command = new MySqlCommand(
                    "UPDATE useriteminfo SET level=@level,exp=@exp WHERE id=@id",
                    connection, transaction);
                command.Parameters.AddWithValue("@level", cell.Level);
                command.Parameters.AddWithValue("@exp", cell.Exp);
                command.Parameters.AddWithValue("@id", cell.RowId);
                await command.ExecuteNonQueryAsync();
            }
        }

        private static async Task DeleteStageLedgerAsync(
            MySqlConnection connection, MySqlTransaction transaction, Guid runId)
        {
            string key = runId.ToString("N");
            foreach (string table in new[]
            {
                "stage_result_cell_settlement_ledger", "stage_result_settlement_ledger"
            })
            {
                await using var command = new MySqlCommand(
                    $"DELETE FROM {table} WHERE run_id=@run AND character_id=9001",
                    connection, transaction);
                command.Parameters.AddWithValue("@run", key);
                await command.ExecuteNonQueryAsync();
            }
        }

        private static bool IsStageClearFrame(byte[] frame) =>
            frame.Length >= 6 && frame[0] == 0x4a && frame[1] == 0 && frame[2] == 2;

        private static bool IsStageResultAck(byte[] frame) =>
            frame.Length >= 6 && frame[0] == 0x53 && frame[1] == 0 &&
            frame[2] == 0 && frame[3] == 1 && frame[4] == 5 && frame[5] == 0;

        private readonly record struct ExpectedProgression(long Exp, uint Gold);
        private readonly record struct StageFixtureSnapshot(
            long Exp, byte Level, byte LevelPoint, uint Gold, byte? Rank);
    }
}
