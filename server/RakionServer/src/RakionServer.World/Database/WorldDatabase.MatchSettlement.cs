using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.World.Database;

public sealed record MatchSettlementEntry(int CharacterId, int Win, int Lose, int Draw);

public sealed partial class WorldDatabase
{
    private const string MatchSettlementTableSql =
        "CREATE TABLE IF NOT EXISTS match_settlement_ledger (" +
        "match_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL," +
        "character_id INT NOT NULL,win TINYINT UNSIGNED NOT NULL," +
        "lose TINYINT UNSIGNED NOT NULL,draw TINYINT UNSIGNED NOT NULL," +
        "created_at DATETIME(6) NOT NULL," +
        "PRIMARY KEY(match_id,character_id)) ENGINE=InnoDB";

    private static Task EnsureMatchSettlementSchemaAsync(MySqlConnection connection) =>
        Exec(connection, MatchSettlementTableSql);

    public async Task EnsureMatchSettlementSchemaAsync()
    {
        await using var connection = new MySqlConnection(_conn);
        await connection.OpenAsync();
        await EnsureMatchSettlementSchemaAsync(connection);
    }

    public async Task<bool> SettleMatchAsync(
        Guid matchId, IReadOnlyCollection<MatchSettlementEntry> entries)
    {
        if (matchId == Guid.Empty || entries.Count == 0) return false;
        if (entries.Select(entry => entry.CharacterId).Distinct().Count() != entries.Count)
            throw new ArgumentException("Personagem duplicado no settlement.", nameof(entries));

        try
        {
            await using var connection = new MySqlConnection(_conn);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            foreach (MatchSettlementEntry entry in entries)
                await ApplySettlementEntryAsync(connection, transaction, matchId, entry);
            await transaction.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("db", "SettleMatchAsync({0}): {1}", matchId, ex.Message);
            return false;
        }
    }

    private static async Task ApplySettlementEntryAsync(
        MySqlConnection connection, MySqlTransaction transaction, Guid matchId,
        MatchSettlementEntry entry)
    {
        string key = matchId.ToString("N");
        await using var insert = new MySqlCommand(
            "INSERT IGNORE INTO match_settlement_ledger " +
            "(match_id,character_id,win,lose,draw,created_at) " +
            "VALUES(@match,@char,@win,@lose,@draw,UTC_TIMESTAMP(6))", connection, transaction);
        AddSettlementParameters(insert, key, entry);
        if (await insert.ExecuteNonQueryAsync() == 0)
        {
            await VerifyExistingSettlementAsync(connection, transaction, key, entry);
            return;
        }

        await using var update = new MySqlCommand(
            "UPDATE characterinfo SET win=win+@win,lose=lose+@lose,draw=draw+@draw " +
            "WHERE id=@char", connection, transaction);
        update.Parameters.AddWithValue("@win", entry.Win);
        update.Parameters.AddWithValue("@lose", entry.Lose);
        update.Parameters.AddWithValue("@draw", entry.Draw);
        update.Parameters.AddWithValue("@char", entry.CharacterId);
        if (await update.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException($"Personagem {entry.CharacterId} não encontrado.");
    }

    private static async Task VerifyExistingSettlementAsync(
        MySqlConnection connection, MySqlTransaction transaction, string key,
        MatchSettlementEntry expected)
    {
        await using var command = new MySqlCommand(
            "SELECT win,lose,draw FROM match_settlement_ledger " +
            "WHERE match_id=@match AND character_id=@char FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("@match", key);
        command.Parameters.AddWithValue("@char", expected.CharacterId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.GetInt32(0) != expected.Win ||
            reader.GetInt32(1) != expected.Lose || reader.GetInt32(2) != expected.Draw)
            throw new InvalidOperationException("Replay de settlement com conteúdo divergente.");
    }

    private static void AddSettlementParameters(
        MySqlCommand command, string key, MatchSettlementEntry entry)
    {
        command.Parameters.AddWithValue("@match", key);
        command.Parameters.AddWithValue("@char", entry.CharacterId);
        command.Parameters.AddWithValue("@win", entry.Win);
        command.Parameters.AddWithValue("@lose", entry.Lose);
        command.Parameters.AddWithValue("@draw", entry.Draw);
    }
}
