using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.World.Database
{
    public sealed partial class WorldDatabase
    {
        public async Task<bool> UpdateCharacterExperienceAsync(int characterId, uint experience)
        {
            if (characterId <= 0)
                return false;
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var command = new MySqlCommand(
                    "UPDATE characterinfo SET exp = @experience WHERE id = @character", connection);
                command.Parameters.AddWithValue("@experience", experience);
                command.Parameters.AddWithValue("@character", characterId);
                return await command.ExecuteNonQueryAsync() == 1;
            }
            catch (Exception ex)
            {
                Log.Error("combat", "UpdateCharacterExperienceAsync(character={0}): {1}",
                    characterId, ex.Message);
                return false;
            }
        }
    }
}
