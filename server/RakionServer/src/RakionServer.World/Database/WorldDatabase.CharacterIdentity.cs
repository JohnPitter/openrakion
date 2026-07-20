using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.World.Database;

public sealed record CharacterIdentity(string AccountId, string BuddyName);
public enum CharacterIdentityLookupStatus : byte { Success, SystemError, NotFound }
public sealed record CharacterIdentityLookupResult(
    CharacterIdentityLookupStatus Status, CharacterIdentity? Identity = null);

public sealed partial class WorldDatabase
{
    public async Task<CharacterIdentityLookupResult> FindCharacterIdentityAsync(string characterName)
    {
        try
        {
            await using var connection = new MySqlConnection(_conn);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "SELECT g.name,COALESCE(NULLIF(g.buddyname,''),c.name) " +
                "FROM usergameinfo g JOIN characterinfo c ON c.userid=g.id " +
                "WHERE c.name=@character LIMIT 1", connection);
            command.Parameters.AddWithValue("@character", characterName);
            await using MySqlDataReader reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return new CharacterIdentityLookupResult(CharacterIdentityLookupStatus.NotFound);
            return new CharacterIdentityLookupResult(
                CharacterIdentityLookupStatus.Success,
                new CharacterIdentity(reader.GetString(0), reader.GetString(1)));
        }
        catch (Exception exception)
        {
            Log.Error("db", "FindCharacterIdentityAsync('{0}'): {1}",
                characterName, exception.Message);
            return new CharacterIdentityLookupResult(CharacterIdentityLookupStatus.SystemError);
        }
    }
}
