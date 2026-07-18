using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    [Collection("E2E")]
    public sealed class StorageCashCouponBundleE2ETests
    {
        private const int CharacterId = 9001;
        private const ushort CashItemId = 1009;
        private const ushort BundleItemId = 9012;

        [Fact]
        public async Task CashCouponAndBundle_PersistWireWalletRowsAndLegacyLedgers()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            await using var sandbox = await StorageEconomyE2ESandbox.CreateAsync(
                fixture.DbConnectionString);

            ItemDef cashItem = RequireItem(fixture.Server!, CashItemId);
            ItemDef bundle = RequireItem(fixture.Server!, BundleItemId);
            int directPrice = RequireCashPrice(cashItem);
            int bundlePrice = RequireCashPrice(bundle);
            CharacterEconomyRules.CouponQuote coupon =
                CharacterEconomyRules.ApplyCoupon(directPrice, 50);
            int[] bundleMembers = fixture.Server!.ExpandSetMembers(BundleItemId).ToArray();
            Assert.Equal(new[] { 1009, 1109, 1209, 1309, 1409, 1509 }, bundleMembers);

            PurchaseRun run;
            await using (var client = await ConnectAsync(fixture, "shop-cash-variants"))
            {
                ClientSession session = Login(client, fixture.Server!);
                Assert.Equal(StorageEconomyE2ESandbox.CouponItemId,
                    session.BoxItems[sandbox.CouponSlot]);
                Assert.Equal(sandbox.CouponRowId, session.BoxRowId[sandbox.CouponSlot]);
                client.OpenInventory();
                client.WaitForNext(IsInventoryOpenAck, JourneyHelper.Timeout);

                PurchaseObservation direct = Purchase(client, session, CashItemId,
                    [CashItemId], StorageEconomyE2ESandbox.WorkingCash - directPrice);
                int pendingAfterDirect = await sandbox.CountNewPendingAsync();
                Assert.InRange(pendingAfterDirect, 0, 1);

                PurchaseObservation discounted = Purchase(client, session, CashItemId,
                    [CashItemId], direct.ExpectedCash - coupon.FinalCost, sandbox.CouponSlot,
                    sandbox.CouponRowId, coupon.LoggedDiscount);
                Assert.Equal(0, session.BoxItems[sandbox.CouponSlot]);
                Assert.Equal(pendingAfterDirect, await sandbox.CountNewPendingAsync());

                PurchaseObservation set = Purchase(client, session, BundleItemId,
                    bundleMembers, discounted.ExpectedCash - bundlePrice);
                int pendingFinal = await sandbox.CountNewPendingAsync();
                Assert.InRange(pendingFinal - pendingAfterDirect, 0, 1);
                Assert.Equal(pendingFinal, client.Received.Count(IsPresentNotification));
                run = new PurchaseRun(direct, discounted, set, pendingFinal);
            }

            JourneyHelper.WaitUntil(
                () => !fixture.Server!.Sessions.Any(value => value.UserId == "test2"),
                "sessão de compra não encerrou");
            await AssertReconnectAsync(fixture, run);
            await AssertDatabaseAsync(sandbox, run, directPrice, coupon, bundlePrice, bundleMembers);
        }

        private static PurchaseObservation Purchase(
            HeadlessWorldClient client, ClientSession session, ushort itemId,
            IReadOnlyList<int> expectedItems, int expectedCash, ushort? couponSlot = null,
            int couponRowId = 0, int couponDiscount = 0)
        {
            var previousRows = session.BoxRowId.Where(value => value > 0).ToHashSet();
            client.BuyStorageItem(itemId, currency: 0, couponSlot);
            byte[] purchase = client.WaitForNext(IsPurchaseAck, JourneyHelper.Timeout);
            byte[] balance = client.WaitForNext(IsPurchaseBalance, JourneyHelper.Timeout);
            Assert.Equal((uint)expectedCash,
                BinaryPrimitives.ReadUInt32LittleEndian(balance.AsSpan(7)));
            Assert.Equal((uint)expectedCash, session.Cash);

            PurchaseFrame frame = ParsePurchaseFrame(purchase, couponSlot.HasValue);
            Assert.Equal(expectedItems, frame.Items);
            Assert.Equal(expectedItems.Count, frame.Cells.Length);
            Assert.Equal(couponRowId, frame.CouponRowId);
            Assert.Equal(couponSlot.HasValue ? StorageEconomyE2ESandbox.CouponItemId : 0,
                frame.CouponItemId);
            Assert.Equal(couponDiscount, frame.CouponDiscount);
            Assert.Equal(couponSlot ?? 0, frame.CouponSlot);
            Assert.Equal(frame.Cells.Length, frame.Cells.Distinct().Count());

            var rowIds = new int[frame.Cells.Length];
            JourneyHelper.WaitUntil(() =>
            {
                for (int i = 0; i < frame.Cells.Length; i++)
                {
                    int cell = frame.Cells[i];
                    if (cell >= session.BoxRowId.Count ||
                        session.BoxItems[cell] != expectedItems[i] ||
                        previousRows.Contains(session.BoxRowId[cell])) return false;
                    rowIds[i] = session.BoxRowId[cell];
                }
                return rowIds.Distinct().Count() == rowIds.Length;
            }, $"grants da compra {itemId} não entraram no storage");
            return new PurchaseObservation(expectedCash, expectedItems.ToArray(), frame.Cells, rowIds);
        }

        private static PurchaseFrame ParsePurchaseFrame(byte[] frame, bool usesCoupon)
        {
            Assert.True(frame.Length >= 21);
            Assert.Equal((ushort)0x14, BinaryPrimitives.ReadUInt16LittleEndian(frame));
            Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(2)));
            Assert.Equal((byte)0, frame[12]);
            Assert.Equal(usesCoupon ? 1 : 0,
                BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(13)));
            Assert.Equal(usesCoupon ? (byte)1 : (byte)0, frame[17]);
            int position = 18;
            int couponRow = 0;
            int couponItem = 0;
            int discount = 0;
            ushort couponSlot = 0;
            if (usesCoupon)
            {
                couponRow = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(position));
                couponItem = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(position + 4));
                discount = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(position + 8));
                couponSlot = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(position + 12));
                position += 14;
            }
            Assert.Equal((byte)0, frame[position++]);
            int count = frame[position++];
            Assert.True(frame.Length >= position + count * 5 + 1);
            var items = new int[count];
            for (int i = 0; i < count; i++, position += 4)
                items[i] = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(position));
            byte[] cells = frame.AsSpan(position, count).ToArray();
            position += count;
            Assert.Equal((byte)0, frame[position]);
            return new PurchaseFrame(couponRow, couponItem, discount, couponSlot, items, cells);
        }

        private static async Task AssertReconnectAsync(WorldServerFixture fixture, PurchaseRun run)
        {
            await using var client = await ConnectAsync(fixture, "shop-cash-reconnect");
            ClientSession session = Login(client, fixture.Server!);
            Assert.Equal((uint)run.Bundle.ExpectedCash, session.Cash);
            foreach (PurchaseObservation purchase in run.All)
                for (int i = 0; i < purchase.RowIds.Length; i++)
                {
                    int cell = session.BoxRowId.IndexOf(purchase.RowIds[i]);
                    Assert.InRange(cell, 0, 119);
                    Assert.Equal(purchase.Items[i], session.BoxItems[cell]);
                }
        }

        private static async Task AssertDatabaseAsync(
            StorageEconomyE2ESandbox sandbox, PurchaseRun run, int directPrice,
            CharacterEconomyRules.CouponQuote coupon, int bundlePrice, int[] bundleMembers)
        {
            Assert.Equal(run.Bundle.ExpectedCash, await sandbox.ReadCashAsync());
            List<PurchasedRow> rows = await sandbox.ReadPurchasedRowsAsync();
            int[] expectedItems = run.All.SelectMany(value => value.Items).ToArray();
            Assert.Equal(expectedItems, rows.Select(value => value.ItemId));
            Assert.All(rows, row => Assert.Equal((byte)2, row.SerialType));

            CouponLogRow couponLog = await sandbox.ReadCouponLogAsync();
            Assert.Equal(StorageEconomyE2ESandbox.CouponItemId, couponLog.CouponItemId);
            Assert.Equal(CashItemId, couponLog.OperationItemId);
            Assert.Equal(coupon.LoggedDiscount, couponLog.Discount);

            List<CashLedgerRow> ledgers = await sandbox.ReadCashLedgersAsync();
            Assert.Equal(rows.Count, ledgers.Count);
            for (int i = 0; i < rows.Count; i++) Assert.Equal(ledgers[i].Id, rows[i].Serial);
            int start = StorageEconomyE2ESandbox.WorkingCash;
            AssertLedger(ledgers[0], CashItemId, directPrice, start, run.Direct.ExpectedCash, "");
            AssertLedger(ledgers[1], CashItemId, coupon.FinalCost, run.Direct.ExpectedCash,
                run.Discounted.ExpectedCash, couponLog.Id.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < bundleMembers.Length; i++)
                AssertLedger(ledgers[i + 2], bundleMembers[i], bundlePrice,
                    run.Discounted.ExpectedCash, run.Bundle.ExpectedCash, "");
            Assert.Equal(run.PendingCount, await sandbox.CountLinkedNewPresentsAsync());
        }

        private static void AssertLedger(
            CashLedgerRow row, int itemId, int price, int previous, int current, string coupon)
        {
            Assert.Equal(itemId, row.ItemId);
            Assert.Equal(price, row.Price);
            Assert.Equal(previous, row.PreviousCash);
            Assert.Equal(current, row.CurrentCash);
            Assert.Equal(coupon, row.CouponLogId.Trim());
        }

        private static ItemDef RequireItem(WorldServer server, ushort itemId) =>
            server.FindItemDef(itemId)
            ?? throw new InvalidOperationException($"Item {itemId} ausente do catálogo.");

        private static int RequireCashPrice(ItemDef item) =>
            StorageEconomyRules.PurchasePrice(item, payGold: false)
            ?? throw new InvalidOperationException($"Item {item.Id} não está à venda por Cash.");

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

        private static bool IsCharacterSelectAck(byte[] frame) =>
            frame.Length >= 3 && frame[0] == 0x14 && frame[1] == 0 && frame[2] == 0;
        private static bool IsInventoryOpenAck(byte[] frame) =>
            frame.Length >= 7 && frame[0] == 0x2c && frame[1] == 0 && frame[2] == 0;
        private static bool IsPurchaseAck(byte[] frame) =>
            frame.Length >= 21 && frame[0] == 0x14 && frame[1] == 0;
        private static bool IsPurchaseBalance(byte[] frame) =>
            frame.Length >= 11 && frame[0] == 0x2e && frame[1] == 0 && frame[2] == 0;
        private static bool IsPresentNotification(byte[] frame) =>
            frame.Length >= 3 && frame[0] == 0x6a && frame[1] == 0;

        private sealed record PurchaseObservation(
            int ExpectedCash, int[] Items, byte[] Cells, int[] RowIds);
        private sealed record PurchaseRun(
            PurchaseObservation Direct, PurchaseObservation Discounted,
            PurchaseObservation Bundle, int PendingCount)
        {
            public IEnumerable<PurchaseObservation> All => [Direct, Discounted, Bundle];
        }
        private readonly record struct PurchaseFrame(
            int CouponRowId, int CouponItemId, int CouponDiscount, ushort CouponSlot,
            int[] Items, byte[] Cells);
    }
}
