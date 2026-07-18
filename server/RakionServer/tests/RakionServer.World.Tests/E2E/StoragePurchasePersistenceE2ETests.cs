using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>Fecha compra, reconnect e venda do storage pelo transporte World real.</summary>
    [Collection("E2E")]
    public sealed class StoragePurchasePersistenceE2ETests
    {
        private const int CharacterId = 9001;
        private const ushort ItemId = 1001;

        [Fact]
        public async Task GoldPurchase_ReconnectsAndSellsWithExactPersistentDeltas()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;

            ItemDef item = fixture.Server!.FindItemDef(ItemId)
                ?? throw new InvalidOperationException("Item 1001 ausente do catálogo.");
            int price = StorageEconomyRules.PurchasePrice(item, payGold: true)
                ?? throw new InvalidOperationException("Item 1001 não está à venda por Gold.");
            int saleCredit = StorageEconomyRules.SellPrice(item, ItemId);
            EconomySnapshot baseline = await ReadBaselineAsync(fixture.DbConnectionString);
            var outcome = new EconomyOutcome(price, saleCredit);
            int purchasedRowId = 0;

            try
            {
                purchasedRowId = await PurchaseAsync(fixture, baseline, outcome);
                await ReconnectAndSellAsync(fixture, baseline, purchasedRowId, outcome);
                await AssertDatabaseAsync(
                    fixture.DbConnectionString, baseline, purchasedRowId, outcome);
            }
            finally
            {
                await RestoreAsync(fixture.DbConnectionString, baseline, purchasedRowId);
            }
        }

        private static async Task<int> PurchaseAsync(
            WorldServerFixture fixture, EconomySnapshot baseline, EconomyOutcome outcome)
        {
            await using var client = await ConnectAsync(fixture, "shop-buy");
            ClientSession session = Login(client, fixture.Server!);
            var existingRows = session.BoxRowId.Where(value => value > 0).ToHashSet();

            client.OpenInventory();
            client.WaitForNext(IsInventoryOpenAck, JourneyHelper.Timeout);
            client.BuyStorageItem(ItemId, currency: 1);
            byte[] purchase = client.WaitForNext(IsPurchaseAck, JourneyHelper.Timeout);
            byte[] balance = client.WaitForNext(IsPurchaseBalance, JourneyHelper.Timeout);

            int rowId = WaitForPurchasedRow(session, existingRows);
            int expectedGold = baseline.Gold - outcome.Price;
            AssertPurchaseFrame(purchase, session, expectedGold);
            Assert.Equal(expectedGold, BinaryPrimitives.ReadInt32LittleEndian(balance.AsSpan(3)));
            Assert.Equal((uint)expectedGold, session.Gold);
            await AssertPurchasePersistedAsync(
                fixture.DbConnectionString, baseline, rowId, expectedGold);
            return rowId;
        }

        private static async Task ReconnectAndSellAsync(
            WorldServerFixture fixture, EconomySnapshot baseline,
            int purchasedRowId, EconomyOutcome outcome)
        {
            JourneyHelper.WaitUntil(
                () => !fixture.Server!.Sessions.Any(value => value.UserId == "test2"),
                "sessão anterior não encerrou");
            await using var client = await ConnectAsync(fixture, "shop-sell");
            ClientSession session = Login(client, fixture.Server!);
            int slot = session.BoxRowId.IndexOf(purchasedRowId);
            Assert.InRange(slot, 0, 119);
            Assert.Equal(ItemId, session.BoxItems[slot]);
            Assert.Equal((uint)(baseline.Gold - outcome.Price), session.Gold);

            client.OpenInventory();
            client.WaitForNext(IsInventoryOpenAck, JourneyHelper.Timeout);
            client.SellStorageItem((byte)slot);
            byte[] sale = client.WaitForNext(IsSaleAck, JourneyHelper.Timeout);

            Assert.Equal((uint)outcome.SaleCredit,
                BinaryPrimitives.ReadUInt32LittleEndian(sale.AsSpan(14)));
            Assert.Equal((byte)slot, sale[18]);
            JourneyHelper.WaitUntil(() => session.BoxRowId[slot] == 0, "venda não limpou o storage");
            Assert.Equal((uint)(baseline.Gold - outcome.Price + outcome.SaleCredit), session.Gold);
        }

        private static ClientSession Login(HeadlessWorldClient client, WorldServer server)
        {
            client.Login("test2", "test2");
            client.WaitForFirstByte(0x0c, JourneyHelper.Timeout);
            client.SelectCharacter(CharacterId);
            client.WaitForNext(IsCharacterSelectAck, JourneyHelper.Timeout);
            return JourneyHelper.WaitForSession(server, "test2",
                value => value.ActiveCharId == CharacterId && value.Status == UserStatus.FieldLobby);
        }

        private static async Task<HeadlessWorldClient> ConnectAsync(
            WorldServerFixture fixture, string name) => await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, name);

        private static int WaitForPurchasedRow(ClientSession session, HashSet<int> existingRows)
        {
            int rowId = 0;
            JourneyHelper.WaitUntil(() =>
            {
                rowId = session.BoxRowId.FirstOrDefault(value => value > 0 && !existingRows.Contains(value));
                return rowId > 0;
            }, "compra não entrou no storage da sessão");
            return rowId;
        }

        private static void AssertPurchaseFrame(byte[] frame, ClientSession session, int expectedGold)
        {
            Assert.Equal((uint)session.GameInfoId, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(4)));
            Assert.Equal((uint)CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8)));
            Assert.Equal(1, frame[12]);
            Assert.Equal(1, frame[19]);
            Assert.Equal(ItemId, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(20)));
            Assert.Equal((uint)expectedGold, session.Gold);
        }

        private static async Task<EconomySnapshot> ReadBaselineAsync(string connectionString)
        {
            await using var connection = await OpenAsync(connectionString);
            int gameInfoId = await ScalarAsync<int>(connection,
                "SELECT id FROM usergameinfo WHERE name='test2'");
            int gold = await ScalarAsync<int>(connection,
                "SELECT gold FROM usergameinfo WHERE id=@id", new SqlArg("@id", gameInfoId));
            long ledgerId = await ScalarAsync<long>(connection,
                "SELECT COALESCE(MAX(id),0) FROM loguseritem WHERE userid=@id",
                new SqlArg("@id", gameInfoId));
            return new EconomySnapshot(gameInfoId, gold, ledgerId);
        }

        private static async Task AssertDatabaseAsync(
            string connectionString, EconomySnapshot baseline,
            int rowId, EconomyOutcome outcome)
        {
            await using var connection = await OpenAsync(connectionString);
            Assert.Equal(baseline.Gold - outcome.Price + outcome.SaleCredit,
                await ScalarAsync<int>(connection,
                    "SELECT gold FROM usergameinfo WHERE id=@id",
                    new SqlArg("@id", baseline.GameInfoId)));
            Assert.Equal(0, await ScalarAsync<int>(connection,
                "SELECT COUNT(*) FROM useriteminfo WHERE id=@id", new SqlArg("@id", rowId)));
            Assert.Equal(2, await ScalarAsync<int>(connection,
                "SELECT COUNT(*) FROM loguseritem WHERE userid=@id AND characterid=@char " +
                "AND itemid=@item AND id>@ledger",
                new SqlArg("@id", baseline.GameInfoId), new SqlArg("@char", CharacterId),
                new SqlArg("@item", ItemId), new SqlArg("@ledger", baseline.LedgerId)));
        }

        private static async Task AssertPurchasePersistedAsync(
            string connectionString, EconomySnapshot baseline, int rowId, int expectedGold)
        {
            await using var connection = await OpenAsync(connectionString);
            Assert.Equal(expectedGold, await ScalarAsync<int>(connection,
                "SELECT gold FROM usergameinfo WHERE id=@id",
                new SqlArg("@id", baseline.GameInfoId)));
            await using var command = Command(connection, null,
                "SELECT item_sn,sn_type FROM useriteminfo WHERE id=@row AND userid=@id",
                new[] { new SqlArg("@row", rowId), new SqlArg("@id", baseline.GameInfoId) });
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), "row comprada não foi persistida");
            long serial = reader.GetInt64(0);
            Assert.True(serial > baseline.LedgerId);
            Assert.Equal((byte)1, reader.GetByte(1));
        }

        private static async Task RestoreAsync(
            string connectionString, EconomySnapshot baseline, int rowId)
        {
            await using var connection = await OpenAsync(connectionString);
            await using var transaction = await connection.BeginTransactionAsync();
            await ExecuteAsync(connection, transaction,
                "DELETE FROM useriteminfo WHERE userid=@id AND (id=@row OR " +
                "(characterid=0 AND itemid=@item AND sn_type=1 AND item_sn>@ledger))",
                new SqlArg("@id", baseline.GameInfoId), new SqlArg("@row", rowId),
                new SqlArg("@item", ItemId), new SqlArg("@ledger", baseline.LedgerId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM loguseritem WHERE userid=@id AND characterid=@char " +
                "AND itemid=@item AND id>@ledger",
                new SqlArg("@id", baseline.GameInfoId), new SqlArg("@char", CharacterId),
                new SqlArg("@item", ItemId), new SqlArg("@ledger", baseline.LedgerId));
            await ExecuteAsync(connection, transaction,
                "UPDATE usergameinfo SET gold=@gold WHERE id=@id",
                new SqlArg("@gold", baseline.Gold), new SqlArg("@id", baseline.GameInfoId));
            await transaction.CommitAsync();
        }

        private static async Task<MySqlConnection> OpenAsync(string connectionString)
        {
            var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            return connection;
        }

        private static async Task<T> ScalarAsync<T>(
            MySqlConnection connection, string sql, params SqlArg[] values)
        {
            await using var command = Command(connection, null, sql, values);
            object? value = await command.ExecuteScalarAsync();
            return (T)Convert.ChangeType(value!, typeof(T));
        }

        private static async Task ExecuteAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            string sql, params SqlArg[] values)
        {
            await using var command = Command(connection, transaction, sql, values);
            await command.ExecuteNonQueryAsync();
        }

        private static MySqlCommand Command(
            MySqlConnection connection, MySqlTransaction? transaction,
            string sql, IReadOnlyList<SqlArg> values)
        {
            var command = new MySqlCommand(sql, connection, transaction);
            foreach (SqlArg value in values)
                command.Parameters.AddWithValue(value.Name, value.Value);
            return command;
        }

        private static bool IsCharacterSelectAck(byte[] frame) =>
            frame.Length >= 3 && frame[0] == 0x14 && frame[1] == 0 && frame[2] == 0;

        private static bool IsInventoryOpenAck(byte[] frame) =>
            frame.Length >= 7 && frame[0] == 0x2c && frame[1] == 0 && frame[2] == 0;

        private static bool IsPurchaseAck(byte[] frame) =>
            frame.Length >= 26 && frame[0] == 0x14 && frame[1] == 0;

        private static bool IsPurchaseBalance(byte[] frame) =>
            frame.Length >= 11 && frame[0] == 0x2e && frame[1] == 0 && frame[2] == 0;

        private static bool IsSaleAck(byte[] frame) =>
            frame.Length >= 31 && frame[0] == 0x15 && frame[1] == 0;

        private readonly record struct EconomySnapshot(int GameInfoId, int Gold, long LedgerId);
        private readonly record struct EconomyOutcome(int Price, int SaleCredit);
        private readonly record struct SqlArg(string Name, object Value);
    }
}
