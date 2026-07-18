using System.Threading.Tasks;
using MySqlConnector;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    internal readonly record struct MatchRecordSnapshot(
        int CharacterId, int Win, int Lose, int Draw);

    internal static class MatchRecordFixture
    {
        public static async Task<MatchRecordSnapshot> ReadAsync(
            string connectionString, int characterId)
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT win,lose,draw FROM characterinfo WHERE id=@id";
            command.Parameters.AddWithValue("@id", characterId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), $"characterinfo id={characterId} ausente");
            return new MatchRecordSnapshot(
                characterId, reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
        }

        public static async Task RestoreAsync(
            string connectionString, params MatchRecordSnapshot[] records)
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            foreach (MatchRecordSnapshot record in records)
            {
                await using var command = new MySqlCommand(
                    "UPDATE characterinfo SET win=@win,lose=@lose,draw=@draw WHERE id=@id",
                    connection, transaction);
                command.Parameters.AddWithValue("@win", record.Win);
                command.Parameters.AddWithValue("@lose", record.Lose);
                command.Parameters.AddWithValue("@draw", record.Draw);
                command.Parameters.AddWithValue("@id", record.CharacterId);
                await command.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }
    }
}
