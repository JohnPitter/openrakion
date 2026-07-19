using System;
using System.Buffers.Binary;
using System.Text;
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

            string name = "E2E" + Guid.NewGuid().ToString("N")[..9];
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
                client.WaitForNextFirstByte(0x14, Timeout);
                client.WaitForNextFirstByte(0x1E, Timeout);
                client.ReturnToCharacterSelect();
                byte slot = await FindFreeCharacterSlotAsync(
                    fixture.DbConnectionString, "test");
                client.CreateCharacter(name, 0, slot);
                byte[] response = client.WaitForNextFirstByte(0x12, Timeout);

                Assert.Equal(0, response[2]);
                int characterId = BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(3));
                Assert.True(characterId > 0);
                Assert.Contains(fixture.Server!.Sessions,
                    session => session.Connected && session.UserId == "test");
                Assert.Equal(characterId,
                    await LoadCharacterIdAsync(fixture.DbConnectionString, name));

                client.SelectCharacter(characterId);
                Assert.Equal(0, client.WaitForNextFirstByte(0x14, Timeout)[2]);
                byte[] channelSnapshot = client.WaitForNextFirstByte(0x1E, Timeout);
                Assert.True(channelSnapshot.AsSpan().IndexOf(Encoding.ASCII.GetBytes(name)) >= 0,
                    $"snapshot 0x1E não contém '{name}': {Convert.ToHexString(channelSnapshot)}");
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

        private static async Task<byte> FindFreeCharacterSlotAsync(
            string connectionString, string accountId)
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "SELECT c.slot FROM characterinfo c JOIN usergameinfo g ON g.id=c.userid " +
                "WHERE g.name=@account AND c.auth<>10", connection);
            command.Parameters.AddWithValue("@account", accountId);
            var occupied = new bool[6];
            await using MySqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int slot = reader.GetInt32(0);
                if (slot >= 0 && slot < occupied.Length) occupied[slot] = true;
            }
            for (byte slot = 0; slot < occupied.Length; slot++)
                if (!occupied[slot]) return slot;
            throw new InvalidOperationException("conta E2E sem slot de personagem livre");
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
