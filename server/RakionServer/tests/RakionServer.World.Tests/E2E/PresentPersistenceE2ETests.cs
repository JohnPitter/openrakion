using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    [Collection("E2E")]
    public sealed class PresentPersistenceE2ETests
    {
        [Fact]
        public async Task OwnershipFifoAcceptDisposeAndReconnect_PersistOnRealWire()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            await using var sandbox = await PresentE2ESandbox.CreateAsync(
                fixture.DbConnectionString);

            await AssertForeignOwnerRejectedAsync(fixture, sandbox);
            await AssertOutOfOrderRejectedAsync(fixture, sandbox);

            AcceptedPresentRow accepted;
            await using (var client = await ConnectAsync(fixture, "present-journey"))
            {
                ClientSession session = Login(client, fixture.Server!, "test2", 9001);
                client.PeekPresent();
                AssertPeek(client.WaitForNext(IsPeek, JourneyHelper.Timeout),
                    0, sandbox.FirstPendingId, PresentE2ESandbox.FirstItemId);

                client.AcceptPresent(sandbox.FirstPendingId, sandbox.AcceptSlot);
                AssertStatus(client.WaitForNext(IsAccept, JourneyHelper.Timeout), 0x6c, 0);
                JourneyHelper.WaitUntil(
                    () => session.BoxItems[sandbox.AcceptSlot] == PresentE2ESandbox.FirstItemId &&
                          session.BoxRowId[sandbox.AcceptSlot] > 0,
                    "aceite não atualizou o storage da sessão");

                client.PeekPresent();
                AssertPeek(client.WaitForNext(IsPeek, JourneyHelper.Timeout),
                    0, sandbox.SecondPendingId, PresentE2ESandbox.SecondItemId);

                client.AcceptPresent(sandbox.SecondPendingId, sandbox.AcceptSlot);
                AssertStatus(client.WaitForNext(IsAccept, JourneyHelper.Timeout), 0x6c, 3);
                Assert.Equal(1, await sandbox.CountPendingAsync());

                client.DisposePresent(sandbox.SecondPendingId);
                AssertStatus(client.WaitForNext(IsDispose, JourneyHelper.Timeout), 0x6d, 0);

                client.PeekPresent();
                AssertPeek(client.WaitForNext(IsPeek, JourneyHelper.Timeout), 1, 0, 0);

                PresentDatabaseState state = await sandbox.ReadStateAsync();
                accepted = AssertDatabaseState(state, sandbox);
                Assert.Equal(accepted.RowId, session.BoxRowId[sandbox.AcceptSlot]);
            }

            JourneyHelper.WaitUntil(
                () => !fixture.Server!.Sessions.Any(session => session.UserId == "test2"),
                "sessão de presentes não encerrou");
            await AssertReconnectAsync(fixture, sandbox, accepted);
        }

        private static async Task AssertForeignOwnerRejectedAsync(
            WorldServerFixture fixture, PresentE2ESandbox sandbox)
        {
            await using var client = await ConnectAsync(fixture, "present-owner-reject");
            ClientSession session = Login(client, fixture.Server!, "test", 1);
            client.DisposePresent(sandbox.FirstPendingId);
            JourneyHelper.WaitUntil(() => !session.Connected,
                "conta estrangeira não foi desconectada ao acessar presente alheio");
            Assert.Equal(2, await sandbox.CountPendingAsync());
        }

        private static async Task AssertOutOfOrderRejectedAsync(
            WorldServerFixture fixture, PresentE2ESandbox sandbox)
        {
            await using var client = await ConnectAsync(fixture, "present-fifo-reject");
            ClientSession session = Login(client, fixture.Server!, "test2", 9001);
            client.DisposePresent(sandbox.SecondPendingId);
            JourneyHelper.WaitUntil(() => !session.Connected,
                "presente fora da ordem FIFO não desconectou a sessão");
            Assert.Equal(2, await sandbox.CountPendingAsync());
        }

        private static AcceptedPresentRow AssertDatabaseState(
            PresentDatabaseState state, PresentE2ESandbox sandbox)
        {
            Assert.Equal(0, state.PendingCount);
            Assert.Equal(PresentE2ESandbox.FirstItemId, state.Accepted.ItemId);
            Assert.Equal(sandbox.AcceptSlot, state.Accepted.Slot);
            Assert.True(state.Accepted.Serial >= 8_000_000);
            Assert.Equal((byte)3, state.Accepted.SerialType);
            Assert.True(state.FirstAudit.Accepted);
            Assert.False(state.FirstAudit.Disposed);
            Assert.False(state.SecondAudit.Accepted);
            Assert.True(state.SecondAudit.Disposed);
            return state.Accepted;
        }

        private static async Task AssertReconnectAsync(
            WorldServerFixture fixture, PresentE2ESandbox sandbox, AcceptedPresentRow accepted)
        {
            await using var client = await ConnectAsync(fixture, "present-reconnect");
            ClientSession session = Login(client, fixture.Server!, "test2", 9001);
            Assert.Equal(accepted.ItemId, session.BoxItems[accepted.Slot]);
            Assert.Equal(accepted.RowId, session.BoxRowId[accepted.Slot]);
            client.PeekPresent();
            AssertPeek(client.WaitForNext(IsPeek, JourneyHelper.Timeout), 1, 0, 0);
        }

        private static void AssertPeek(
            byte[] frame, byte status, int pendingId, int itemId)
        {
            Assert.True(frame.Length >= 13);
            Assert.Equal((ushort)0x6b, BinaryPrimitives.ReadUInt16LittleEndian(frame));
            Assert.Equal(status, frame[2]);
            Assert.Equal(pendingId, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(3)));
            Assert.Equal(itemId, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(7)));
            Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(11)));
        }

        private static void AssertStatus(byte[] frame, ushort opcode, byte status)
        {
            Assert.True(frame.Length >= 3);
            Assert.Equal(opcode, BinaryPrimitives.ReadUInt16LittleEndian(frame));
            Assert.Equal(status, frame[2]);
        }

        private static ClientSession Login(
            HeadlessWorldClient client, WorldServer server, string account, int characterId)
        {
            client.Login(account, account);
            client.WaitForFirstByte(0x0c, JourneyHelper.Timeout);
            client.SelectCharacter(characterId);
            client.WaitForNext(IsCharacterSelectAck, JourneyHelper.Timeout);
            client.WaitForNext(IsChannelSnapshot, JourneyHelper.Timeout);
            return JourneyHelper.WaitForSession(server, account,
                value => value.ActiveCharId == characterId &&
                         value.Status == UserStatus.FieldLobby);
        }

        private static async Task<HeadlessWorldClient> ConnectAsync(
            WorldServerFixture fixture, string name) => await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, name);

        private static bool IsCharacterSelectAck(byte[] frame) =>
            frame.Length >= 3 && frame[0] == 0x14 && frame[1] == 0 && frame[2] == 0;
        private static bool IsChannelSnapshot(byte[] frame) =>
            frame.Length >= 2 && frame[0] == 0x1e && frame[1] == 0;
        private static bool IsPeek(byte[] frame) =>
            frame.Length >= 2 && frame[0] == 0x6b && frame[1] == 0;
        private static bool IsAccept(byte[] frame) =>
            frame.Length >= 2 && frame[0] == 0x6c && frame[1] == 0;
        private static bool IsDispose(byte[] frame) =>
            frame.Length >= 2 && frame[0] == 0x6d && frame[1] == 0;
    }
}
