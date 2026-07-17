using System.Threading.Tasks;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>
    /// Valida o relay de datagramas de COMBATE (não só movimento) entre dois peers
    /// headless: ataque (animation 0x0311) e sync de estado (0x030F) do master chegam
    /// byte a byte ao joiner. É a extensão do PvP-no-fio para ação de combate.
    /// </summary>
    [Collection("E2E")]
    public sealed class TwoClientCombatRelayTests
    {
        [Fact]
        public async Task AttackAndSyncDatagrams_RelayToOtherPeer()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return; // skip suave
            var server = fixture.Server!;

            await using var master = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "master");
            await using var joiner = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "joiner");

            var (ms, _, _) = JourneyHelper.DriveToUdpReadyRoom(
                server, master, joiner, HeadlessWorldClient.RoomSpec.Golem("e2e-combat"), fixture.UdpPort2);

            // Ataque (0x0311, kind=Attack) do master → relay ao joiner.
            byte[] attack = master.SendAttack(fixture.UdpPort2, ms.FieldSeat, kind: 1);
            byte[] relayedAttack = joiner.WaitForUdp(
                p => p.Length == 10 && p[0] == 0x11 && p[1] == 0x03, JourneyHelper.Timeout);
            Assert.Equal(attack, relayedAttack);
            Assert.Equal(ms.FieldSeat, relayedAttack[6]);
            Assert.Equal(1, relayedAttack[8]); // kind Attack preservado

            // Sync de estado (0x030F) do master → relay ao joiner.
            byte[] sync = master.SendSync(fixture.UdpPort2, ms.FieldSeat, lifeState: 1);
            byte[] relayedSync = joiner.WaitForUdp(
                p => p.Length == 14 && p[0] == 0x0f && p[1] == 0x03, JourneyHelper.Timeout);
            Assert.Equal(sync, relayedSync);
            Assert.Equal(ms.FieldSeat, relayedSync[6]);
        }
    }
}
