using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>
    /// Valida a camada UDP de gameplay com dois clientes headless reais: ambos autenticam
    /// o endpoint UDP (handshake 0x0202) contra o servidor vivo, e um movimento 0x030A de
    /// um jogador é RELAYADO para o outro peer do mesmo field. Prova no fio o handshake de
    /// gameplay, o registro do endpoint e o relay peer→peer — o núcleo do PvP em runtime,
    /// sem cliente gráfico.
    /// </summary>
    [Collection("E2E")]
    public sealed class TwoClientGameplayUdpTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task TwoHeadlessClients_UdpHandshakeAndMoveRelay_ReachesOtherPeer()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return; // skip suave
            var server = fixture.Server!;

            await using var master = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "master");
            await using var joiner = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "joiner");

            master.Login("test", "test");
            joiner.Login("test2", "test2");
            master.WaitForFirstByte(0x0C, Timeout);
            joiner.WaitForFirstByte(0x0C, Timeout);

            master.SelectCharacter(1);
            joiner.SelectCharacter(9001);
            ClientSession masterSession = WaitForSession(server, "test",
                s => s.ActiveCharId > 0 && s.Status == UserStatus.FieldLobby, Timeout);
            ClientSession joinerSession = WaitForSession(server, "test2",
                s => s.ActiveCharId > 0 && s.Status == UserStatus.FieldLobby, Timeout);

            master.CreateGolemRoom("e2e-udp");
            WaitUntil(() => masterSession.FieldId >= 0 && server.GetField(masterSession.FieldId) != null, Timeout, "sala não criada");
            int fieldId = masterSession.FieldId;
            joiner.JoinRoom((ushort)fieldId);
            WaitUntil(() => joinerSession.FieldId == fieldId, Timeout, "joiner não entrou");

            // Handshake UDP dos dois: registra os endpoints de gameplay no servidor.
            master.OpenUdp();
            joiner.OpenUdp();
            master.UdpHandshake(fixture.UdpPort2, masterSession.Slot, masterSession.UdpKey);
            joiner.UdpHandshake(fixture.UdpPort2, joinerSession.Slot, joinerSession.UdpKey);

            WaitUntil(() => masterSession.UdpEndpoint != null && joinerSession.UdpEndpoint != null, Timeout,
                "endpoints UDP não foram autenticados");

            // O echo do handshake (0x0201, 12 bytes) volta a ambos.
            master.WaitForUdp(p => p.Length == 12 && p[0] == 0x01 && p[1] == 0x02, Timeout);
            joiner.WaitForUdp(p => p.Length == 12 && p[0] == 0x01 && p[1] == 0x02, Timeout);

            // Master (assento 0) manda movimento 0x030A; o servidor relaya ao OUTRO peer.
            byte[] sent = master.SendMove(fixture.UdpPort2, masterSession.FieldSeat, 100, 0, 250);
            byte[] relayed = joiner.WaitForUdp(
                p => p.Length == 26 && p[0] == 0x0a && p[1] == 0x03, Timeout);

            Assert.Equal(sent, relayed);            // relay íntegro byte a byte
            Assert.Equal(masterSession.FieldSeat, relayed[6]); // assento de origem preservado
        }

        private static ClientSession WaitForSession(
            WorldServer server, string account, Func<ClientSession, bool> ready, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                ClientSession? s = server.Sessions.FirstOrDefault(
                    x => string.Equals(x.UserId, account, StringComparison.OrdinalIgnoreCase));
                if (s != null && ready(s)) return s;
                Thread.Sleep(100);
            }
            throw new TimeoutException($"sessão '{account}' não atingiu o estado esperado em {timeout.TotalSeconds:0.#}s");
        }

        private static void WaitUntil(Func<bool> condition, TimeSpan timeout, string message)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return;
                Thread.Sleep(100);
            }
            throw new TimeoutException(message + $" (timeout {timeout.TotalSeconds:0.#}s)");
        }
    }
}
