using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Database;

public sealed partial class WorldDatabase
{
    private static async Task VerifyCellProgressionRowsAsync(
        MySqlConnection connection, MySqlTransaction transaction, int gameInfoId,
        IReadOnlyList<CellProgressionChange> cells)
    {
        foreach (CellProgressionChange cell in cells)
        {
            if (cell.Before.RowId <= 0) continue;
            await using var command = new MySqlCommand(
                "SELECT userid,itemid,level,exp FROM useriteminfo WHERE id=@row FOR UPDATE",
                connection, transaction);
            command.Parameters.AddWithValue("@row", cell.Before.RowId);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync() || reader.GetInt32(0) != gameInfoId ||
                reader.GetInt32(1) != cell.Before.ItemId ||
                reader.GetByte(2) != cell.Before.Level || reader.GetInt64(3) != cell.Before.Exp)
                throw new InvalidOperationException("Estado da cell divergiu do banco.");
        }
    }

    private static async Task UpdateCellProgressionRowsAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        IReadOnlyList<CellProgressionChange> cells)
    {
        foreach (CellProgressionChange cell in cells)
        {
            if (cell.Before.RowId <= 0 || cell.AppliedExp == 0) continue;
            await using var command = new MySqlCommand(
                "UPDATE useriteminfo SET level=@level,exp=@exp WHERE id=@row",
                connection, transaction);
            command.Parameters.AddWithValue("@level", cell.After.Level);
            command.Parameters.AddWithValue("@exp", cell.After.Exp);
            command.Parameters.AddWithValue("@row", cell.After.RowId);
            if (await command.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("Cell não foi atualizada.");
        }
    }

    public async Task<Dictionary<(int Npc, byte Level), long>> LoadCellLevelCurveAsync()
    {
        var result = new Dictionary<(int, byte), long>();
        try
        {
            await using var connection = new MySqlConnection(_conn);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "SELECT npc,level,exp FROM npcinfo WHERE level BETWEEN 0 AND 255", connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result[(reader.GetInt32(0), checked((byte)reader.GetInt32(1)))] = reader.GetInt64(2);
        }
        catch (Exception ex)
        {
            Log.Error("db", "LoadCellLevelCurveAsync: {0}", ex.Message);
        }
        return result;
    }
}
