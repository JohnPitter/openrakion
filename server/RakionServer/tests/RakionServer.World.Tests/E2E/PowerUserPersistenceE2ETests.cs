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
    public sealed class PowerUserPersistenceE2ETests
    {
        [Fact]
        public async Task InitialRenewalCouponReconnectAndExpiration_PersistOnRealWire()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            await using var sandbox = await PowerUserE2ESandbox.CreateAsync(
                fixture.DbConnectionString);

            PowerUserAck initial;
            PowerUserAck renewal;
            await using (var client = await ConnectAsync(fixture, "power-user-purchase"))
            {
                ClientSession session = Login(client, fixture.Server!);
                Assert.False(session.PuActive);

                client.BuyPowerUser(mode: 0);
                initial = ParseSuccess(client.WaitForNext(IsPowerUserAck, JourneyHelper.Timeout));
                Assert.Equal(PowerUserE2ESandbox.WorkingCash - 8_000, initial.Cash);
                Assert.Equal(5, initial.Points);
                AssertSession(session, initial, active: true);

                client.BuyPowerUser(mode: 1, sandbox.CouponSlot);
                renewal = ParseSuccess(client.WaitForNext(IsPowerUserAck, JourneyHelper.Timeout));
                Assert.Equal(initial.Cash - 3_000, renewal.Cash);
                Assert.Equal(initial.Points + 5, renewal.Points);
                Assert.Equal(initial.Marker + 30u * 24 * 60, renewal.Marker);
                AssertSession(session, renewal, active: true);
                Assert.Equal((uint)7, session.BonusExp(5));
                Assert.Equal((uint)100, session.BonusGold(100));
            }

            JourneyHelper.WaitUntil(
                () => !fixture.Server!.Sessions.Any(session => session.UserId == "test2"),
                "sessão de compra Power User não encerrou");
            PowerUserDatabaseState state = await sandbox.ReadStateAsync();
            AssertDatabaseState(state, initial, renewal);

            await using var reconnect = await ConnectAsync(fixture, "power-user-reconnect");
            ClientSession reloaded = Login(reconnect, fixture.Server!);
            AssertSession(reloaded, renewal, active: true);
            Assert.Equal(state.Expires, reloaded.PuExpiresAt);

            await sandbox.ExpireAsync();
            JourneyHelper.WaitUntil(() => !reloaded.PuActive && !reloaded.ExpBonusActive,
                "expiração Power User não foi recarregada durante a sessão");
            Assert.Equal((uint)5, reloaded.BonusExp(5));
        }

        private static void AssertDatabaseState(
            PowerUserDatabaseState state, PowerUserAck initial, PowerUserAck renewal)
        {
            Assert.Equal(renewal.Cash, state.Cash);
            Assert.Equal(renewal.Points, state.Points);
            Assert.Equal(renewal.Marker, checked((uint)state.Marker));
            Assert.True(state.Expires > DateTime.Now.AddDays(59));
            Assert.Equal(0, state.CouponRows);
            Assert.Equal(2, state.Ledgers.Count);

            PowerUserLedgerRow first = state.Ledgers[0];
            Assert.Equal((byte)0, first.Mode);
            Assert.Equal(8_000, first.Cost);
            Assert.Equal(0, first.PreviousMarker);
            Assert.Equal(initial.Marker, checked((uint)first.CurrentMarker));
            Assert.Equal(0, first.PreviousPoints);
            Assert.Equal(5, first.CurrentPoints);
            Assert.Empty(first.CouponLogId);

            PowerUserLedgerRow second = state.Ledgers[1];
            Assert.Equal((byte)1, second.Mode);
            Assert.Equal(3_000, second.Cost);
            Assert.Equal(initial.Marker, checked((uint)second.PreviousMarker));
            Assert.Equal(renewal.Marker, checked((uint)second.CurrentMarker));
            Assert.Equal(5, second.PreviousPoints);
            Assert.Equal(10, second.CurrentPoints);
            Assert.NotEmpty(second.CouponLogId);
        }

        private static void AssertSession(
            ClientSession session, PowerUserAck ack, bool active)
        {
            Assert.Equal(ack.Gold, checked((int)session.Gold));
            Assert.Equal(ack.Cash, checked((int)session.Cash));
            Assert.Equal(ack.Points, checked((int)session.PowerLevelPoint));
            Assert.Equal(active, session.PuActive);
            Assert.Equal(active, session.ExpBonusActive);
        }

        private static PowerUserAck ParseSuccess(byte[] frame)
        {
            Assert.True(frame.Length >= 18);
            Assert.Equal((ushort)0x34, BinaryPrimitives.ReadUInt16LittleEndian(frame));
            Assert.Equal((byte)0, frame[2]);
            Assert.Equal((byte)0, frame[17]);
            Assert.All(frame.AsSpan(18).ToArray(), value => Assert.Equal((byte)0, value));
            return new PowerUserAck(
                BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(3)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(7)),
                BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(11)),
                BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(15)));
        }

        private static ClientSession Login(HeadlessWorldClient client, WorldServer server)
        {
            client.Login("test2", "test2");
            client.WaitForFirstByte(0x0c, JourneyHelper.Timeout);
            client.SelectCharacter(9001);
            client.WaitForNext(IsCharacterSelectAck, JourneyHelper.Timeout);
            client.WaitForNext(IsChannelSnapshot, JourneyHelper.Timeout);
            return JourneyHelper.WaitForSession(server, "test2",
                value => value.ActiveCharId == 9001 && value.Status == UserStatus.FieldLobby);
        }

        private static async Task<HeadlessWorldClient> ConnectAsync(
            WorldServerFixture fixture, string name) => await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, name);

        private static bool IsCharacterSelectAck(byte[] frame) =>
            frame.Length >= 3 && frame[0] == 0x14 && frame[1] == 0 && frame[2] == 0;
        private static bool IsChannelSnapshot(byte[] frame) =>
            frame.Length >= 2 && frame[0] == 0x1e && frame[1] == 0;
        private static bool IsPowerUserAck(byte[] frame) =>
            frame.Length >= 3 && frame[0] == 0x34 && frame[1] == 0;

        private readonly record struct PowerUserAck(int Gold, int Cash, uint Marker, ushort Points);
    }
}
