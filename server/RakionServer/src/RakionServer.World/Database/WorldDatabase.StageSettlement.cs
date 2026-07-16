using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Database;

public enum StageSettlementStatus { Failed, Applied, Replay }

public readonly record struct StageRunIdentity(
    Guid RunId, int CharacterId, int GameInfoId, byte StageId);

public readonly record struct StageResultAward(
    byte Rank, uint ReportedExp, uint ReportedGold,
    uint CellExpSlot1, uint CellExpSlot2, uint CellExpSlot3,
    uint AppliedExp, uint AppliedGold);

public sealed record StageSettlementRequest(
    StageRunIdentity Identity, StageResultAward Award,
    CharacterProgressionState Before, CharacterProgressionState After,
    uint GoldBefore, uint GoldAfter)
{
    public IReadOnlyList<CellProgressionChange> Cells { get; init; } =
        Array.Empty<CellProgressionChange>();
}

public readonly record struct StageSettlementResult(
    StageSettlementStatus Status, byte LevelBefore,
    CharacterProgressionState Progression, uint Gold, byte BestRank)
{
    public int LevelUps => Math.Max(0, Progression.Level - LevelBefore);
}

public sealed partial class WorldDatabase
{
    private const string StageSettlementTableSql =
        "CREATE TABLE IF NOT EXISTS stage_result_settlement_ledger (" +
        "run_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL," +
        "character_id INT NOT NULL,usergameinfo_id INT NOT NULL,stage_id TINYINT UNSIGNED NOT NULL," +
        "rank_value TINYINT UNSIGNED NOT NULL,reported_exp INT UNSIGNED NOT NULL," +
        "reported_gold INT UNSIGNED NOT NULL,metric_a INT UNSIGNED NOT NULL DEFAULT 0," +
        "metric_b INT UNSIGNED NOT NULL DEFAULT 0,metric_c INT UNSIGNED NOT NULL DEFAULT 0," +
        "applied_exp INT UNSIGNED NOT NULL," +
        "applied_gold INT UNSIGNED NOT NULL,exp_after BIGINT NOT NULL," +
        "level_before TINYINT UNSIGNED NOT NULL,level_after TINYINT UNSIGNED NOT NULL," +
        "level_point_after TINYINT UNSIGNED NOT NULL,gold_after INT UNSIGNED NOT NULL," +
        "best_rank_after TINYINT UNSIGNED NOT NULL,created_at DATETIME(6) NOT NULL," +
        "PRIMARY KEY(run_id,character_id)) ENGINE=InnoDB";

    private const string StageCellSettlementTableSql =
        "CREATE TABLE IF NOT EXISTS stage_result_cell_settlement_ledger (" +
        "run_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL," +
        "character_id INT NOT NULL,cell_index TINYINT UNSIGNED NOT NULL," +
        "slot_no TINYINT UNSIGNED NOT NULL,row_id INT NOT NULL,item_id INT NOT NULL," +
        "reported_exp INT UNSIGNED NOT NULL,applied_exp INT UNSIGNED NOT NULL," +
        "level_before TINYINT UNSIGNED NOT NULL,level_after TINYINT UNSIGNED NOT NULL," +
        "exp_before BIGINT NOT NULL,exp_after BIGINT NOT NULL," +
        "PRIMARY KEY(run_id,character_id,cell_index)) ENGINE=InnoDB";

    private static async Task EnsureStageSettlementSchemaAsync(MySqlConnection connection)
    {
        await Exec(connection,
            "DELETE old FROM userstageinfo old JOIN userstageinfo keep " +
            "ON keep.characterid=old.characterid AND keep.stage=old.stage " +
            "AND (keep.rank>old.rank OR (keep.rank=old.rank AND keep.id<old.id))");
        await Exec(connection, "ALTER TABLE userstageinfo ENGINE=InnoDB");
        await Exec(connection, "ALTER TABLE userstageinfo ADD UNIQUE INDEX IF NOT EXISTS " +
            "ux_userstageinfo_character_stage (characterid,stage)");
        await Exec(connection, StageSettlementTableSql);
        await Exec(connection, StageCellSettlementTableSql);
        await Exec(connection, "ALTER TABLE stage_result_settlement_ledger " +
            "ADD COLUMN IF NOT EXISTS metric_a INT UNSIGNED NOT NULL DEFAULT 0 AFTER reported_gold");
        await Exec(connection, "ALTER TABLE stage_result_settlement_ledger " +
            "ADD COLUMN IF NOT EXISTS metric_b INT UNSIGNED NOT NULL DEFAULT 0 AFTER metric_a");
        await Exec(connection, "ALTER TABLE stage_result_settlement_ledger " +
            "ADD COLUMN IF NOT EXISTS metric_c INT UNSIGNED NOT NULL DEFAULT 0 AFTER metric_b");
    }

    public async Task EnsureStageSettlementSchemaAsync()
    {
        await using var connection = new MySqlConnection(_conn);
        await connection.OpenAsync();
        await EnsureStageSettlementSchemaAsync(connection);
    }

    public async Task<IReadOnlyList<StageDefinition>> LoadStageCatalogAsync()
    {
        var stages = new List<StageDefinition>();
        await using var connection = new MySqlConnection(_conn);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT id,maxcharacters,minlevel,maxlevel FROM stageinfo ORDER BY id", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int id = reader.GetInt32(0);
            int players = reader.GetInt32(1);
            int min = reader.GetInt32(2);
            int max = reader.GetInt32(3);
            if (id is < 1 or > byte.MaxValue || players is < 1 or > byte.MaxValue ||
                min is < 1 or > byte.MaxValue || max is < 1 or > byte.MaxValue)
                throw new InvalidOperationException($"stageinfo inválido no stage {id}.");
            stages.Add(new StageDefinition((byte)id, (byte)players, (byte)min, (byte)max));
        }
        return stages;
    }

    public async Task<StageSettlementResult> SettleStageResultAsync(StageSettlementRequest request)
    {
        if (!IsValidStageSettlement(request)) return FailedStageSettlement(request.Before);
        try
        {
            await using var connection = new MySqlConnection(_conn);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            StageSettlementResult result = await ApplyStageSettlementAsync(
                connection, transaction, request);
            await transaction.CommitAsync();
            return result;
        }
        catch (Exception ex)
        {
            Log.Error("db", "SettleStageResultAsync({0}/{1}): {2}",
                request.Identity.RunId, request.Identity.CharacterId, ex.Message);
            return FailedStageSettlement(request.Before);
        }
    }

    private static async Task<StageSettlementResult> ApplyStageSettlementAsync(
        MySqlConnection connection, MySqlTransaction transaction, StageSettlementRequest request)
    {
        byte currentRank = await ReadStageRankAsync(connection, transaction, request.Identity);
        byte bestRank = Math.Max(currentRank, request.Award.Rank);
        await using var insert = BuildStageLedgerInsert(
            connection, transaction, request, bestRank);
        if (await insert.ExecuteNonQueryAsync() == 0)
        {
            StageSettlementResult replay = await ReadStageReplayAsync(
                connection, transaction, request);
            await VerifyStageCellReplayAsync(connection, transaction, request);
            return replay;
        }

        await VerifyStageProgressionAsync(connection, transaction, request);
        await VerifyCellProgressionRowsAsync(
            connection, transaction, request.Identity.GameInfoId, request.Cells);
        await UpdateStageProgressionAsync(connection, transaction, request, bestRank);
        await UpdateCellProgressionRowsAsync(connection, transaction, request.Cells);
        await InsertStageCellLedgerAsync(connection, transaction, request);
        return new StageSettlementResult(StageSettlementStatus.Applied, request.Before.Level,
            request.After, request.GoldAfter, bestRank);
    }

    private static async Task<byte> ReadStageRankAsync(
        MySqlConnection connection, MySqlTransaction transaction, StageRunIdentity identity)
    {
        await using var command = new MySqlCommand(
            "SELECT `rank` FROM userstageinfo WHERE characterid=@char AND stage=@stage FOR UPDATE",
            connection, transaction);
        command.Parameters.AddWithValue("@char", identity.CharacterId);
        command.Parameters.AddWithValue("@stage", identity.StageId);
        object? value = await command.ExecuteScalarAsync();
        return value is null ? (byte)0 : checked((byte)Math.Clamp(Convert.ToInt32(value), 0, 5));
    }

    private static MySqlCommand BuildStageLedgerInsert(
        MySqlConnection connection, MySqlTransaction transaction,
        StageSettlementRequest request, byte bestRank)
    {
        var command = new MySqlCommand(
            "INSERT IGNORE INTO stage_result_settlement_ledger " +
            "(run_id,character_id,usergameinfo_id,stage_id,rank_value,reported_exp,reported_gold," +
            "metric_a,metric_b,metric_c,applied_exp,applied_gold,exp_after,level_before,level_after,level_point_after," +
            "gold_after,best_rank_after,created_at) VALUES " +
            "(@run,@char,@game,@stage,@rank,@reportedExp,@reportedGold,@metricA,@metricB,@metricC,@appliedExp,@appliedGold," +
            "@expAfter,@levelBefore,@levelAfter,@pointAfter,@goldAfter,@bestRank,UTC_TIMESTAMP(6))",
            connection, transaction);
        AddStageParameters(command, request);
        command.Parameters.AddWithValue("@bestRank", bestRank);
        return command;
    }

    private static async Task<StageSettlementResult> ReadStageReplayAsync(
        MySqlConnection connection, MySqlTransaction transaction, StageSettlementRequest request)
    {
        await using var command = new MySqlCommand(
            "SELECT usergameinfo_id,stage_id,rank_value,reported_exp,reported_gold,applied_exp," +
            "applied_gold,metric_a,metric_b,metric_c,exp_after,level_before,level_after,level_point_after,gold_after," +
            "best_rank_after FROM stage_result_settlement_ledger " +
            "WHERE run_id=@run AND character_id=@char FOR UPDATE", connection, transaction);
        AddStageKey(command, request.Identity);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.GetInt32(0) != request.Identity.GameInfoId ||
            reader.GetByte(1) != request.Identity.StageId || reader.GetByte(2) != request.Award.Rank ||
            reader.GetUInt32(3) != request.Award.ReportedExp ||
            reader.GetUInt32(4) != request.Award.ReportedGold ||
            reader.GetUInt32(5) != request.Award.AppliedExp ||
            reader.GetUInt32(6) != request.Award.AppliedGold ||
            reader.GetUInt32(7) != request.Award.CellExpSlot1 ||
            reader.GetUInt32(8) != request.Award.CellExpSlot2 ||
            reader.GetUInt32(9) != request.Award.CellExpSlot3)
            throw new InvalidOperationException("Replay de stage com conteúdo divergente.");

        var progression = new CharacterProgressionState(
            reader.GetInt64(10), reader.GetByte(12), reader.GetByte(13));
        return new StageSettlementResult(StageSettlementStatus.Replay, reader.GetByte(11),
            progression, reader.GetUInt32(14), reader.GetByte(15));
    }

    private static async Task VerifyStageProgressionAsync(
        MySqlConnection connection, MySqlTransaction transaction, StageSettlementRequest request)
    {
        await using var character = new MySqlCommand(
            "SELECT exp,level,levelpoint FROM characterinfo WHERE id=@char FOR UPDATE",
            connection, transaction);
        character.Parameters.AddWithValue("@char", request.Identity.CharacterId);
        await using (var reader = await character.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync() || reader.GetInt64(0) != request.Before.Exp ||
                reader.GetByte(1) != request.Before.Level ||
                reader.GetByte(2) != request.Before.LevelPoint)
                throw new InvalidOperationException("Progressão do stage divergiu do banco.");
        }

        await using var wallet = new MySqlCommand(
            "SELECT gold FROM usergameinfo WHERE id=@game FOR UPDATE", connection, transaction);
        wallet.Parameters.AddWithValue("@game", request.Identity.GameInfoId);
        object? gold = await wallet.ExecuteScalarAsync();
        if (gold is null || Convert.ToUInt32(gold) != request.GoldBefore)
            throw new InvalidOperationException("Gold do stage divergiu do banco.");
    }

    private static async Task UpdateStageProgressionAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        StageSettlementRequest request, byte bestRank)
    {
        await using (var character = new MySqlCommand(
            "UPDATE characterinfo SET exp=@exp,level=@level,levelpoint=@point WHERE id=@char",
            connection, transaction))
        {
            character.Parameters.AddWithValue("@exp", request.After.Exp);
            character.Parameters.AddWithValue("@level", request.After.Level);
            character.Parameters.AddWithValue("@point", request.After.LevelPoint);
            character.Parameters.AddWithValue("@char", request.Identity.CharacterId);
            if (await character.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("Personagem do stage não foi atualizado.");
        }

        await using (var wallet = new MySqlCommand(
            "UPDATE usergameinfo SET gold=@gold WHERE id=@game", connection, transaction))
        {
            wallet.Parameters.AddWithValue("@gold", request.GoldAfter);
            wallet.Parameters.AddWithValue("@game", request.Identity.GameInfoId);
            if (await wallet.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("Carteira do stage não foi atualizada.");
        }

        if (bestRank == 0) return;
        await using var rank = new MySqlCommand(
            "INSERT INTO userstageinfo(characterid,stage,`rank`,updatetime) " +
            "VALUES(@char,@stage,@rank,NOW()) ON DUPLICATE KEY UPDATE " +
            "`rank`=GREATEST(`rank`,VALUES(`rank`)),updatetime=NOW()", connection, transaction);
        rank.Parameters.AddWithValue("@char", request.Identity.CharacterId);
        rank.Parameters.AddWithValue("@stage", request.Identity.StageId);
        rank.Parameters.AddWithValue("@rank", bestRank);
        await rank.ExecuteNonQueryAsync();
    }

    private static async Task InsertStageCellLedgerAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        StageSettlementRequest request)
    {
        for (int index = 0; index < request.Cells.Count; index++)
        {
            CellProgressionChange cell = request.Cells[index];
            await using var command = new MySqlCommand(
                "INSERT INTO stage_result_cell_settlement_ledger " +
                "(run_id,character_id,cell_index,slot_no,row_id,item_id,reported_exp," +
                "applied_exp,level_before,level_after,exp_before,exp_after) VALUES " +
                "(@run,@char,@index,@slot,@row,@item,@reported,@applied,@level0,@level1,@exp0,@exp1)",
                connection, transaction);
            AddStageKey(command, request.Identity);
            AddStageCellParameters(command, index, cell);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task VerifyStageCellReplayAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        StageSettlementRequest request)
    {
        await using var command = new MySqlCommand(
            "SELECT cell_index,slot_no,row_id,item_id,reported_exp,applied_exp,level_before," +
            "level_after,exp_before,exp_after FROM stage_result_cell_settlement_ledger " +
            "WHERE run_id=@run AND character_id=@char ORDER BY cell_index FOR UPDATE",
            connection, transaction);
        AddStageKey(command, request.Identity);
        await using var reader = await command.ExecuteReaderAsync();
        int index = 0;
        while (await reader.ReadAsync())
        {
            if (index >= request.Cells.Count ||
                !StageCellMatches(reader, request.Cells[index], index))
                throw new InvalidOperationException("Replay de Cell EXP do stage divergente.");
            index++;
        }
        if (index != request.Cells.Count)
            throw new InvalidOperationException("Replay de Cell EXP do stage incompleto.");
    }

    private static bool StageCellMatches(
        System.Data.Common.DbDataReader reader, CellProgressionChange cell, int index) =>
        reader.GetInt32(0) == index && reader.GetByte(1) == cell.Before.Slot &&
        reader.GetInt32(2) == cell.Before.RowId && reader.GetInt32(3) == cell.Before.ItemId &&
        Convert.ToUInt32(reader.GetValue(4)) == cell.ReportedExp &&
        Convert.ToUInt32(reader.GetValue(5)) == cell.AppliedExp &&
        reader.GetByte(6) == cell.Before.Level && reader.GetByte(7) == cell.After.Level &&
        reader.GetInt64(8) == cell.Before.Exp && reader.GetInt64(9) == cell.After.Exp;

    private static void AddStageCellParameters(
        MySqlCommand command, int index, CellProgressionChange cell)
    {
        command.Parameters.AddWithValue("@index", index);
        command.Parameters.AddWithValue("@slot", cell.Before.Slot);
        command.Parameters.AddWithValue("@row", cell.Before.RowId);
        command.Parameters.AddWithValue("@item", cell.Before.ItemId);
        command.Parameters.AddWithValue("@reported", cell.ReportedExp);
        command.Parameters.AddWithValue("@applied", cell.AppliedExp);
        command.Parameters.AddWithValue("@level0", cell.Before.Level);
        command.Parameters.AddWithValue("@level1", cell.After.Level);
        command.Parameters.AddWithValue("@exp0", cell.Before.Exp);
        command.Parameters.AddWithValue("@exp1", cell.After.Exp);
    }

    private static void AddStageParameters(MySqlCommand command, StageSettlementRequest request)
    {
        AddStageKey(command, request.Identity);
        command.Parameters.AddWithValue("@game", request.Identity.GameInfoId);
        command.Parameters.AddWithValue("@stage", request.Identity.StageId);
        command.Parameters.AddWithValue("@rank", request.Award.Rank);
        command.Parameters.AddWithValue("@reportedExp", request.Award.ReportedExp);
        command.Parameters.AddWithValue("@reportedGold", request.Award.ReportedGold);
        command.Parameters.AddWithValue("@metricA", request.Award.CellExpSlot1);
        command.Parameters.AddWithValue("@metricB", request.Award.CellExpSlot2);
        command.Parameters.AddWithValue("@metricC", request.Award.CellExpSlot3);
        command.Parameters.AddWithValue("@appliedExp", request.Award.AppliedExp);
        command.Parameters.AddWithValue("@appliedGold", request.Award.AppliedGold);
        command.Parameters.AddWithValue("@expAfter", request.After.Exp);
        command.Parameters.AddWithValue("@levelBefore", request.Before.Level);
        command.Parameters.AddWithValue("@levelAfter", request.After.Level);
        command.Parameters.AddWithValue("@pointAfter", request.After.LevelPoint);
        command.Parameters.AddWithValue("@goldAfter", request.GoldAfter);
    }

    private static void AddStageKey(MySqlCommand command, StageRunIdentity identity)
    {
        command.Parameters.AddWithValue("@run", identity.RunId.ToString("N"));
        command.Parameters.AddWithValue("@char", identity.CharacterId);
    }

    private static bool IsValidStageSettlement(StageSettlementRequest request) =>
        request.Identity.RunId != Guid.Empty && request.Identity.CharacterId > 0 &&
        request.Identity.GameInfoId > 0 && request.Identity.StageId > 0 &&
        request.Award.Rank <= 5 && request.After.Exp >= request.Before.Exp &&
        request.GoldAfter >= request.GoldBefore && request.GoldAfter <= int.MaxValue &&
        (request.Cells.Count == 0 || request.Cells.Count == 3 &&
            request.Cells.Select(cell => cell.Before.Slot).Distinct().Count() == 3 &&
            request.Cells.All(IsValidStageCell));

    private static bool IsValidStageCell(CellProgressionChange cell)
    {
        bool sameIdentity = cell.After.Slot == cell.Before.Slot &&
            cell.After.RowId == cell.Before.RowId && cell.After.ItemId == cell.Before.ItemId &&
            cell.After.ItemHandle == cell.Before.ItemHandle;
        if (!sameIdentity || cell.Before.Exp < 0 || cell.After.Exp < 0 ||
            cell.After.Level < cell.Before.Level || cell.AppliedExp > cell.ReportedExp)
            return false;

        bool eligible = cell.Before.RowId > 0 && cell.Before.ItemId is >= 8000 and < 9000 &&
            cell.ReportedExp > 0;
        if (!eligible) return cell.AppliedExp == 0 && cell.After == cell.Before;
        try
        {
            long awardedTotal = checked(cell.Before.Exp + cell.AppliedExp);
            return cell.AppliedExp == cell.ReportedExp &&
                (cell.Before.Level < 99
                    ? cell.After.Exp == awardedTotal
                    : cell.After.Exp <= awardedTotal);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static StageSettlementResult FailedStageSettlement(CharacterProgressionState state) =>
        new(StageSettlementStatus.Failed, state.Level, state, 0, 0);
}
