using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using MySqlConnector;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    [Collection("E2E")]
    public sealed class CharacterCreateE2ETests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task ReturnFromLobbyThenCreateCharacterKeepsSessionConnected()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;

            string name = "E2E" + Guid.NewGuid().ToString("N")[..7];
            try
            {
                await using var client = await HeadlessWorldClient.ConnectAsync(
                    WorldServerFixture.Host, fixture.TcpPort, "character-create");
                client.Login("test", "test");
                client.WaitForFirstByte(0x0C, Timeout);
                client.DrainReceived();

                int selectedId = await LoadCharacterIdAsync(
                    fixture.DbConnectionString, "GoHeroi");
                client.SelectCharacter(selectedId);
                client.WaitForFirstByte(0x14, Timeout);
                client.ReturnToCharacterSelect();
                client.CreateCharacter(name, 0, 1);
                byte[] response = client.WaitForFirstByte(0x12, Timeout);

                Assert.Equal(0, response[2]);
                int characterId = BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(3));
                Assert.True(characterId > 0);
                Assert.Contains(fixture.Server!.Sessions,
                    session => session.Connected && session.UserId == "test");
                Assert.Equal(characterId,
                    await LoadCharacterIdAsync(fixture.DbConnectionString, name));
            }
            finally
            {
                await DeleteCharacterAsync(fixture.DbConnectionString, name);
            }
        }

        private static async Task<int> LoadCharacterIdAsync(string connectionString, string name)
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "SELECT id FROM characterinfo WHERE name=@name LIMIT 1", connection);
            command.Parameters.AddWithValue("@name", name);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private static async Task DeleteCharacterAsync(string connectionString, string name)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return;
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "DELETE FROM characterinfo WHERE name=@name", connection);
            command.Parameters.AddWithValue("@name", name);
            await command.ExecuteNonQueryAsync();
        }
    }
}
