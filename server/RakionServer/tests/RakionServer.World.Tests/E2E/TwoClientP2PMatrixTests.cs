using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>Prova a matriz local de rota direta e fallback TCP sem confundir topologia externa.</summary>
    [Collection("E2E")]
    public sealed class TwoClientP2PMatrixTests
    {
        [Fact]
        public async Task DirectPeers_ExchangeUdpWithoutWorldAndTcpTunnelDoesNotDuplicate()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            await using var master = await ConnectAsync(fixture, "direct-master");
            await using var joiner = await ConnectAsync(fixture, "direct-joiner");

            var (masterSession, joinerSession, field) =
                DriveToPlayingMatch(fixture, master, joiner, joinerDirect: true);
            Assert.False(field.HasTunnelingClient);

            byte[] direct = master.SendDirectMove(
                joiner.UdpLocalEndpoint, masterSession.FieldSeat, 321, 0, -123);
            Assert.Equal(direct, joiner.WaitForUdp(IsMove, JourneyHelper.Timeout));

            master.SendTunnelOne(joinerSession.FieldSeat, direct);
            AssertNoTunnelFrame(joiner);
            master.SendTunnelAll(direct);
            AssertNoTunnelFrame(joiner);
        }

        [Fact]
        public async Task DirectAndTunneledPeers_RelayOneAndAllOnlyAcrossFallbackPair()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            await using var master = await ConnectAsync(fixture, "tunnel-master");
            await using var joiner = await ConnectAsync(fixture, "tunnel-joiner");

            var (masterSession, joinerSession, field) =
                DriveToPlayingMatch(fixture, master, joiner, joinerDirect: false);
            Assert.True(field.HasTunnelingClient);
            Assert.False(field.FindRec(masterSession)!.UsesTunneling);
            Assert.True(field.FindRec(joinerSession)!.UsesTunneling);

            byte[] oneToTunnel = { 0x0a, 0x03, 0x11, 0x22 };
            master.SendTunnelOne(joinerSession.FieldSeat, oneToTunnel);
            AssertTunnel(joiner.WaitForNext(IsTunnelFrame, JourneyHelper.Timeout), oneToTunnel);

            byte[] oneToDirect = { 0x0f, 0x03, 0x33, 0x44 };
            joiner.SendTunnelOne(masterSession.FieldSeat, oneToDirect);
            AssertTunnel(master.WaitForNext(IsTunnelFrame, JourneyHelper.Timeout), oneToDirect);

            byte[] allFromDirect = { 0x11, 0x03, 0x55, 0x66 };
            master.SendTunnelAll(allFromDirect);
            AssertTunnel(joiner.WaitForNext(IsTunnelFrame, JourneyHelper.Timeout), allFromDirect);

            byte[] allFromTunnel = { 0x0a, 0x03, 0x77, 0x00 };
            joiner.SendTunnelAll(allFromTunnel);
            AssertTunnel(master.WaitForNext(IsTunnelFrame, JourneyHelper.Timeout), allFromTunnel);
        }

        private static async Task<HeadlessWorldClient> ConnectAsync(
            WorldServerFixture fixture, string name) =>
            await HeadlessWorldClient.ConnectAsync(WorldServerFixture.Host, fixture.TcpPort, name);

        private static (ClientSession master, ClientSession joiner, Field field) DriveToPlayingMatch(
            WorldServerFixture fixture, HeadlessWorldClient master,
            HeadlessWorldClient joiner, bool joinerDirect)
        {
            WorldServer server = fixture.Server!;
            master.Login("test", "test");
            joiner.Login("test2", "test2");
            master.WaitForFirstByte(0x0c, JourneyHelper.Timeout);
            joiner.WaitForFirstByte(0x0c, JourneyHelper.Timeout);
            master.SelectCharacter(1);
            joiner.SelectCharacter(9001);
            ClientSession ms = JourneyHelper.WaitForSession(server, "test",
                value => value.ActiveCharId > 0 && value.Status == UserStatus.FieldLobby);
            ClientSession js = JourneyHelper.WaitForSession(server, "test2",
                value => value.ActiveCharId > 0 && value.Status == UserStatus.FieldLobby);

            AuthenticateDirectEndpoint(master, ms, fixture.UdpPort2);
            if (joinerDirect) AuthenticateDirectEndpoint(joiner, js, fixture.UdpPort2);

            master.CreateRoom(HeadlessWorldClient.RoomSpec.Golem("e2e-p2p-matrix"));
            JourneyHelper.WaitUntil(
                () => ms.FieldId >= 0 && server.GetField(ms.FieldId) != null, "sala não criada");
            Field field = server.GetField(ms.FieldId)!;
            joiner.JoinRoom((ushort)field.Id);
            JourneyHelper.WaitUntil(() => js.FieldId == field.Id, "joiner não entrou");
            joiner.ChangeTeam();
            JourneyHelper.WaitUntil(() => js.FieldSeat >= 10, "joiner não trocou de time");
            joiner.SetReady(true);
            JourneyHelper.WaitUntil(() => field.FindRec(js)?.LobbyReady == true, "joiner não ficou ready");
            master.StartMatch();
            JourneyHelper.WaitUntil(() => field.MatchId != Guid.Empty, "partida não armou");
            master.EnterField();
            joiner.EnterField();
            master.SpawnField();
            joiner.SpawnField();
            JourneyHelper.WaitUntil(() => field.Phase == MatchPhase.Playing, "round não iniciou");
            return (ms, js, field);
        }

        private static void AuthenticateDirectEndpoint(
            HeadlessWorldClient client, ClientSession session, int udpPort)
        {
            client.OpenUdp();
            client.UdpHandshake(udpPort, session.Slot, session.UdpKey);
            JourneyHelper.WaitUntil(() => session.UdpObservedEndpoint != null, "endpoint não autenticado");
            client.WaitForUdp(IsHandshakeEcho, JourneyHelper.Timeout);
        }

        private static void AssertNoTunnelFrame(HeadlessWorldClient client)
        {
            try
            {
                client.WaitForNext(IsTunnelFrame, TimeSpan.FromMilliseconds(500));
                Assert.Fail("par direto/direto não deveria receber fallback TCP");
            }
            catch (TimeoutException) { }
        }

        private static void AssertTunnel(byte[] frame, byte[] expected)
        {
            Assert.Equal(expected.Length, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(2)));
            Assert.Equal(expected, frame.AsSpan(4).ToArray());
        }

        private static bool IsHandshakeEcho(byte[] packet) =>
            packet.Length == 12 && packet[0] == 0x01 && packet[1] == 0x02;

        private static bool IsMove(byte[] packet) =>
            packet.Length == 26 && packet[0] == 0x0a && packet[1] == 0x03;

        private static bool IsTunnelFrame(byte[] frame) =>
            frame.Length >= 4 && frame[0] == 0x57 && frame[1] == 0 &&
            frame.Length == 4 + BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(2));
    }
}
