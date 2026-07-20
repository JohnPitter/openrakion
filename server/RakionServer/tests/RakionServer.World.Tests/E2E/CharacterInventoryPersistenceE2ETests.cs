using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    [Collection("E2E")]
    public sealed class CharacterInventoryPersistenceE2ETests
    {
        private const int SpiderItemId = 8000;
        private const int PotionItemId = 12003;

        [Fact]
        public async Task CharacterSelection_HydratesItemsWithoutStealingOtherLoadout()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            await using var sandbox = await InventorySandbox.CreateAsync(fixture.DbConnectionString);

            await using var client = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "inventory-character-select");
            client.Login("test2", "test2");
            client.WaitForFirstByte(0x0c, JourneyHelper.Timeout);
            client.SelectCharacter(sandbox.NewCharacterId);
            client.WaitForNext(IsCharacterSelectAck, JourneyHelper.Timeout);

            ClientSession session = JourneyHelper.WaitForSession(fixture.Server!, "test2",
                value => value.ActiveCharId == sandbox.NewCharacterId);
            Assert.Contains(session.Items, item => item.Id == sandbox.SpiderRowId);
            Assert.Contains(session.Items, item => item.Id == sandbox.NewCharacterPotionRowId);
            byte[] potionPaint = client.WaitForNextFirstByte(0x31, JourneyHelper.Timeout);
            Assert.True(IsSelectedPotionPaint(potionPaint), Convert.ToHexString(potionPaint));
            await sandbox.AssertPositionsAsync();
        }

        private static bool IsCharacterSelectAck(byte[] frame) =>
            frame.Length >= 3 && frame[0] == 0x14 && frame[1] == 0 && frame[2] == 0;

        private static bool IsSelectedPotionPaint(byte[] frame) =>
            frame.Length >= 21 && BinaryPrimitives.ReadUInt16LittleEndian(frame) == 0x31 &&
            frame[12] == 1 && frame[13] == 13 &&
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(14)) == PotionItemId;

        private sealed class InventorySandbox : IAsyncDisposable
        {
            private readonly string _connectionString;
            private readonly int _gameInfoId;
            private readonly int _originalCharacterId;
            private readonly string _originalCharacterName;

            public int NewCharacterId { get; private set; }
            public int SpiderRowId { get; private set; }
            public int OriginalCharacterPotionRowId { get; private set; }
            public int NewCharacterPotionRowId { get; private set; }

            private InventorySandbox(
                string connectionString, int gameInfoId,
                int originalCharacterId, string originalCharacterName)
            {
                _connectionString = connectionString;
                _gameInfoId = gameInfoId;
                _originalCharacterId = originalCharacterId;
                _originalCharacterName = originalCharacterName;
            }

            public static async Task<InventorySandbox> CreateAsync(string connectionString)
            {
                await using var connection = await OpenAsync(connectionString);
                await using var read = new MySqlCommand(
                    "SELECT u.id,c.id,u.charname FROM usergameinfo u " +
                    "JOIN characterinfo c ON c.userid=u.id AND c.name=u.charname " +
                    "WHERE u.name='test2' LIMIT 1", connection);
                await using var reader = await read.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync(), "fixture test2 sem personagem ativo");
                int gameInfoId = reader.GetInt32(0);
                int originalCharacterId = reader.GetInt32(1);
                string originalCharacterName = reader.GetString(2);
                await reader.DisposeAsync();

                var sandbox = new InventorySandbox(
                    connectionString, gameInfoId, originalCharacterId, originalCharacterName);
                await sandbox.InsertAsync(connection);
                return sandbox;
            }

            public async Task AssertPositionsAsync()
            {
                await using var connection = await OpenAsync(_connectionString);
                Assert.Equal(_originalCharacterId,
                    await ReadCharacterIdAsync(connection, OriginalCharacterPotionRowId));
                Assert.Equal(NewCharacterId,
                    await ReadCharacterIdAsync(connection, NewCharacterPotionRowId));
                Assert.Equal(NewCharacterId, await ReadCharacterIdAsync(connection, SpiderRowId));
            }

            private async Task InsertAsync(MySqlConnection connection)
            {
                string characterName = "InvHyd" + Guid.NewGuid().ToString("N")[..6];
                await using var character = new MySqlCommand(
                    "INSERT INTO characterinfo(userid,name,class,level,slot,potionslot,createtime,changetime) " +
                    "VALUES(@user,@name,1,10,1,3,NOW(),NOW())", connection);
                character.Parameters.AddWithValue("@user", _gameInfoId);
                character.Parameters.AddWithValue("@name", characterName);
                await character.ExecuteNonQueryAsync();
                NewCharacterId = checked((int)character.LastInsertedId);

                long serial = await NextSerialAsync(connection);
                OriginalCharacterPotionRowId = await InsertItemAsync(
                    connection, _originalCharacterId, PotionItemId, 13, serial++);
                NewCharacterPotionRowId = await InsertItemAsync(
                    connection, NewCharacterId, PotionItemId, 13, serial++);
                SpiderRowId = await InsertItemAsync(
                    connection, NewCharacterId, SpiderItemId, 10, serial);
            }

            public async ValueTask DisposeAsync()
            {
                await using var connection = await OpenAsync(_connectionString);
                await using var transaction = await connection.BeginTransactionAsync();
                await ExecuteAsync(connection, transaction,
                    "DELETE FROM useriteminfo WHERE id IN (@first,@second,@spider)",
                    ("@first", OriginalCharacterPotionRowId),
                    ("@second", NewCharacterPotionRowId), ("@spider", SpiderRowId));
                await ExecuteAsync(connection, transaction,
                    "DELETE FROM characterinfo WHERE id=@character", ("@character", NewCharacterId));
                await ExecuteAsync(connection, transaction,
                    "UPDATE usergameinfo SET charname=@name WHERE id=@user",
                    ("@name", _originalCharacterName), ("@user", _gameInfoId));
                await transaction.CommitAsync();
            }

            private static async Task<int> InsertItemAsync(
                MySqlConnection connection, int characterId, int itemId, int slot, long serial)
            {
                await using var command = new MySqlCommand(
                    "INSERT INTO useriteminfo(userid,characterid,itemid,item_sn,sn_type,level,limittime,slot) " +
                    "SELECT userid,@character,@item,@serial,3,1,0,@slot FROM characterinfo WHERE id=@character",
                    connection);
                command.Parameters.AddWithValue("@character", characterId);
                command.Parameters.AddWithValue("@item", itemId);
                command.Parameters.AddWithValue("@serial", serial);
                command.Parameters.AddWithValue("@slot", slot);
                await command.ExecuteNonQueryAsync();
                return checked((int)command.LastInsertedId);
            }

            private static async Task<long> NextSerialAsync(MySqlConnection connection)
            {
                await using var command = new MySqlCommand(
                    "SELECT COALESCE(MAX(item_sn),0)+1 FROM useriteminfo WHERE sn_type=3", connection);
                return Convert.ToInt64(await command.ExecuteScalarAsync());
            }

            private static async Task<int> ReadCharacterIdAsync(
                MySqlConnection connection, int rowId)
            {
                await using var command = new MySqlCommand(
                    "SELECT characterid FROM useriteminfo WHERE id=@id", connection);
                command.Parameters.AddWithValue("@id", rowId);
                return Convert.ToInt32(await command.ExecuteScalarAsync());
            }

            private static async Task ExecuteAsync(
                MySqlConnection connection, MySqlTransaction transaction,
                string sql, params (string Name, object Value)[] parameters)
            {
                await using var command = new MySqlCommand(sql, connection, transaction);
                foreach (var parameter in parameters)
                    command.Parameters.AddWithValue(parameter.Name, parameter.Value);
                await command.ExecuteNonQueryAsync();
            }

            private static async Task<MySqlConnection> OpenAsync(string connectionString)
            {
                var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();
                return connection;
            }
        }
    }
}
