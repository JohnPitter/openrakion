using System;
using System.Linq;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Database;

public enum GamePointSettlementStatus { Failed, Applied, Replay }

public readonly record struct GamePointIdentity(
    Guid MatchId, byte Round, int CharacterId, int GameInfoId);

public readonly record struct GamePointAward(uint Exp, uint Gold);

public sealed record GamePointSettlementRequest(
    GamePointIdentity Identity, GamePointAward Award,
    CharacterProgressionState Before, CharacterProgressionState After)
{
    public System.Collections.Generic.IReadOnlyList<CellProgressionChange> Cells { get; init; } =
        System.Array.Empty<CellProgressionChange>();
}

public readonly record struct GamePointSettlementResult(
    GamePointSettlementStatus Status, byte LevelBefore, CharacterProgressionState Progression)
{
    public int LevelUps => Math.Max(0, Progression.Level - LevelBefore);
}

public sealed partial class WorldDatabase
{
    private const string GamePointSettlementTableSql =
        "CREATE TABLE IF NOT EXISTS game_point_settlement_ledger (" +
        "match_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL," +
        "round_no TINYINT UNSIGNED NOT NULL,character_id INT NOT NULL," +
        "usergameinfo_id INT NOT NULL,exp INT UNSIGNED NOT NULL,gold INT UNSIGNED NOT NULL," +
        "exp_before BIGINT NOT NULL,exp_after BIGINT NOT NULL," +
        "level_before TINYINT UNSIGNED NOT NULL,level_after TINYINT UNSIGNED NOT NULL," +
        "level_point_before TINYINT UNSIGNED NOT NULL,level_point_after TINYINT UNSIGNED NOT NULL," +
        "created_at DATETIME(6) NOT NULL," +
        "PRIMARY KEY(match_id,round_no,character_id)) ENGINE=InnoDB";

    private const string GamePointCellSettlementTableSql =
        "CREATE TABLE IF NOT EXISTS game_point_cell_settlement_ledger (" +
        "match_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL," +
        "round_no TINYINT UNSIGNED NOT NULL,character_id INT NOT NULL," +
        "cell_index TINYINT UNSIGNED NOT NULL,slot_no TINYINT UNSIGNED NOT NULL," +
        "row_id INT NOT NULL,item_id INT NOT NULL,reported_exp INT UNSIGNED NOT NULL," +
        "applied_exp INT UNSIGNED NOT NULL,level_before TINYINT UNSIGNED NOT NULL," +
        "level_after TINYINT UNSIGNED NOT NULL,exp_before BIGINT NOT NULL,exp_after BIGINT NOT NULL," +
        "PRIMARY KEY(match_id,round_no,character_id,cell_index)) ENGINE=InnoDB";

    private static async Task EnsureGamePointSettlementSchemaAsync(MySqlConnection connection)
    {
        await Exec(connection, GamePointSettlementTableSql);
        await Exec(connection, GamePointCellSettlementTableSql);
    }

    public async Task EnsureGamePointSettlementSchemaAsync()
    {
        await using var connection = new MySqlConnection(_conn);
        await connection.OpenAsync();
        await EnsureGamePointSettlementSchemaAsync(connection);
    }

    public async Task<GamePointSettlementResult> SettleGamePointAsync(
        GamePointSettlementRequest request)
    {
        if (!IsValid(request)) return Failed(request.Before);
        try
        {
            await using var connection = new MySqlConnection(_conn);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            GamePointSettlementResult result = await ApplyGamePointAsync(
                connection, transaction, request);
            await transaction.CommitAsync();
            return result;
        }
        catch (Exception ex)
        {
            Log.Error("db", "SettleGamePointAsync({0}/{1}/{2}): {3}",
                request.Identity.MatchId, request.Identity.Round,
                request.Identity.CharacterId, ex.Message);
            return Failed(request.Before);
        }
    }

    private static async Task<GamePointSettlementResult> ApplyGamePointAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        GamePointSettlementRequest request)
    {
        await using var insert = BuildGamePointInsert(connection, transaction, request);
        if (await insert.ExecuteNonQueryAsync() == 0)
        {
            GamePointSettlementResult replay = await ReadReplayAsync(connection, transaction, request);
            await VerifyCellReplayAsync(connection, transaction, request);
            return replay;
        }

        await VerifyProgressionRowsAsync(connection, transaction, request);
        await VerifyCellProgressionRowsAsync(
            connection, transaction, request.Identity.GameInfoId, request.Cells);
        await UpdateProgressionAsync(connection, transaction, request);
        await UpdateCellProgressionRowsAsync(connection, transaction, request.Cells);
        await InsertCellLedgerAsync(connection, transaction, request);
        return new GamePointSettlementResult(
            GamePointSettlementStatus.Applied, request.Before.Level, request.After);
    }

    private static MySqlCommand BuildGamePointInsert(
        MySqlConnection connection, MySqlTransaction transaction,
        GamePointSettlementRequest request)
    {
        var command = new MySqlCommand(
            "INSERT IGNORE INTO game_point_settlement_ledger " +
            "(match_id,round_no,character_id,usergameinfo_id,exp,gold,exp_before,exp_after," +
            "level_before,level_after,level_point_before,level_point_after,created_at) VALUES" +
            "(@match,@round,@char,@game,@exp,@gold,@exp0,@exp1,@level0,@level1,@point0,@point1,UTC_TIMESTAMP(6))",
            connection, transaction);
        AddGamePointParameters(command, request);
        return command;
    }

    private static async Task<GamePointSettlementResult> ReadReplayAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        GamePointSettlementRequest request)
    {
        await using var command = new MySqlCommand(
            "SELECT usergameinfo_id,exp,gold,exp_after,level_before,level_after,level_point_after " +
            "FROM game_point_settlement_ledger WHERE match_id=@match AND round_no=@round " +
            "AND character_id=@char FOR UPDATE", connection, transaction);
        AddGamePointKey(command, request.Identity);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.GetInt32(0) != request.Identity.GameInfoId ||
            reader.GetUInt32(1) != request.Award.Exp || reader.GetUInt32(2) != request.Award.Gold)
            throw new InvalidOperationException("Replay de game point com conteúdo divergente.");

        var progression = new CharacterProgressionState(
            reader.GetInt64(3), reader.GetByte(5), reader.GetByte(6));
        return new GamePointSettlementResult(
            GamePointSettlementStatus.Replay, reader.GetByte(4), progression);
    }

    private static async Task VerifyProgressionRowsAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        GamePointSettlementRequest request)
    {
        await using var character = new MySqlCommand(
            "SELECT exp,level,levelpoint FROM characterinfo WHERE id=@char FOR UPDATE",
            connection, transaction);
        character.Parameters.AddWithValue("@char", request.Identity.CharacterId);
        await using (var reader = await character.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("Personagem do game point não encontrado.");
            if (reader.GetInt64(0) != request.Before.Exp || reader.GetByte(1) != request.Before.Level ||
                reader.GetByte(2) != request.Before.LevelPoint)
                throw new InvalidOperationException("Progressão em memória divergiu do banco.");
        }

        await using var wallet = new MySqlCommand(
            "SELECT id FROM usergameinfo WHERE id=@game FOR UPDATE", connection, transaction);
        wallet.Parameters.AddWithValue("@game", request.Identity.GameInfoId);
        if (await wallet.ExecuteScalarAsync() is null)
            throw new InvalidOperationException("Carteira do game point não encontrada.");
    }

    private static async Task UpdateProgressionAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        GamePointSettlementRequest request)
    {
        await using var character = new MySqlCommand(
            "UPDATE characterinfo SET exp=@exp,level=@level,levelpoint=@point WHERE id=@char",
            connection, transaction);
        character.Parameters.AddWithValue("@exp", request.After.Exp);
        character.Parameters.AddWithValue("@level", request.After.Level);
        character.Parameters.AddWithValue("@point", request.After.LevelPoint);
        character.Parameters.AddWithValue("@char", request.Identity.CharacterId);
        await character.ExecuteNonQueryAsync();

        await using var wallet = new MySqlCommand(
            "UPDATE usergameinfo SET gold=gold+@gold WHERE id=@game", connection, transaction);
        wallet.Parameters.AddWithValue("@gold", request.Award.Gold);
        wallet.Parameters.AddWithValue("@game", request.Identity.GameInfoId);
        await wallet.ExecuteNonQueryAsync();
    }

    private static async Task InsertCellLedgerAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        GamePointSettlementRequest request)
    {
        for (int index = 0; index < request.Cells.Count; index++)
        {
            CellProgressionChange cell = request.Cells[index];
            await using var command = new MySqlCommand(
                "INSERT INTO game_point_cell_settlement_ledger " +
                "(match_id,round_no,character_id,cell_index,slot_no,row_id,item_id,reported_exp," +
                "applied_exp,level_before,level_after,exp_before,exp_after) VALUES" +
                "(@match,@round,@char,@index,@slot,@row,@item,@reported,@applied,@level0,@level1,@exp0,@exp1)",
                connection, transaction);
            AddGamePointKey(command, request.Identity);
            AddCellParameters(command, index, cell);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task VerifyCellReplayAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        GamePointSettlementRequest request)
    {
        await using var command = new MySqlCommand(
            "SELECT cell_index,slot_no,row_id,item_id,reported_exp,applied_exp,level_before," +
            "level_after,exp_before,exp_after FROM game_point_cell_settlement_ledger " +
            "WHERE match_id=@match AND round_no=@round AND character_id=@char " +
            "ORDER BY cell_index FOR UPDATE", connection, transaction);
        AddGamePointKey(command, request.Identity);
        await using var reader = await command.ExecuteReaderAsync();
        int index = 0;
        while (await reader.ReadAsync())
        {
            if (index >= request.Cells.Count || !CellMatches(reader, request.Cells[index], index))
                throw new InvalidOperationException("Replay de cell EXP com conteúdo divergente.");
            index++;
        }
        if (index != request.Cells.Count)
            throw new InvalidOperationException("Replay de cell EXP incompleto.");
    }

    private static bool CellMatches(
        System.Data.Common.DbDataReader reader, CellProgressionChange cell, int index) =>
        reader.GetInt32(0) == index && reader.GetByte(1) == cell.Before.Slot &&
        reader.GetInt32(2) == cell.Before.RowId && reader.GetInt32(3) == cell.Before.ItemId &&
        Convert.ToUInt32(reader.GetValue(4)) == cell.ReportedExp &&
        Convert.ToUInt32(reader.GetValue(5)) == cell.AppliedExp &&
        reader.GetByte(6) == cell.Before.Level && reader.GetByte(7) == cell.After.Level &&
        reader.GetInt64(8) == cell.Before.Exp && reader.GetInt64(9) == cell.After.Exp;

    private static void AddCellParameters(
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

    private static void AddGamePointParameters(
        MySqlCommand command, GamePointSettlementRequest request)
    {
        AddGamePointKey(command, request.Identity);
        command.Parameters.AddWithValue("@game", request.Identity.GameInfoId);
        command.Parameters.AddWithValue("@exp", request.Award.Exp);
        command.Parameters.AddWithValue("@gold", request.Award.Gold);
        command.Parameters.AddWithValue("@exp0", request.Before.Exp);
        command.Parameters.AddWithValue("@exp1", request.After.Exp);
        command.Parameters.AddWithValue("@level0", request.Before.Level);
        command.Parameters.AddWithValue("@level1", request.After.Level);
        command.Parameters.AddWithValue("@point0", request.Before.LevelPoint);
        command.Parameters.AddWithValue("@point1", request.After.LevelPoint);
    }

    private static void AddGamePointKey(MySqlCommand command, GamePointIdentity identity)
    {
        command.Parameters.AddWithValue("@match", identity.MatchId.ToString("N"));
        command.Parameters.AddWithValue("@round", identity.Round);
        command.Parameters.AddWithValue("@char", identity.CharacterId);
    }

    private static bool IsValid(GamePointSettlementRequest request)
    {
        if (request.Identity.MatchId == Guid.Empty || request.Identity.Round == 0 ||
            request.Identity.CharacterId <= 0 || request.Identity.GameInfoId <= 0 ||
            request.Before.Exp < 0 || request.After.Level < request.Before.Level)
            return false;
        if (request.Cells.Count != 0 && (request.Cells.Count != 3 ||
            request.Cells.Select(cell => cell.Before.Slot).Distinct().Count() != 3 ||
            request.Cells.Any(cell => !IsValidCell(cell))))
            return false;
        try
        {
            return request.After.Exp == checked(request.Before.Exp + request.Award.Exp);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsValidCell(CellProgressionChange cell)
    {
        bool sameIdentity = cell.After.Slot == cell.Before.Slot &&
            cell.After.RowId == cell.Before.RowId && cell.After.ItemId == cell.Before.ItemId &&
            cell.After.ItemHandle == cell.Before.ItemHandle;
        if (!sameIdentity || cell.Before.Exp < 0 || cell.After.Exp < 0 ||
            cell.After.Level < cell.Before.Level || cell.AppliedExp > 100 ||
            cell.AppliedExp > cell.ReportedExp)
            return false;

        bool eligible = cell.Before.RowId > 0 && cell.Before.ItemId is >= 8000 and < 9000 &&
            cell.ReportedExp > 0;
        if (!eligible) return cell.AppliedExp == 0 && cell.After == cell.Before;
        try
        {
            long awardedTotal = checked(cell.Before.Exp + cell.AppliedExp);
            return cell.AppliedExp == Math.Min(cell.ReportedExp, 100u) &&
                (cell.Before.Level < 99
                    ? cell.After.Exp == awardedTotal
                    : cell.After.Exp <= awardedTotal);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static GamePointSettlementResult Failed(CharacterProgressionState progression) =>
        new(GamePointSettlementStatus.Failed, progression.Level, progression);
}
