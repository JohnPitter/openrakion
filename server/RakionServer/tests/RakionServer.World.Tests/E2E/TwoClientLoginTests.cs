using System;
using System.Linq;
using System.Threading.Tasks;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>
    /// Validação dinâmica via backend com DOIS clientes headless: eles atravessam o
    /// transporte real (TCP + AES + framing + dispatch por opcode) contra um
    /// <see cref="WorldServer"/> vivo. Prova que o servidor porta a autenticação e o
    /// login v258 ponta a ponta, não só no nível de domínio.
    ///
    /// Suave por design: sem banco acessível a suíte faz skip (igual aos
    /// *DatabaseSmokeTests). Com o stack de dev de pé (root/123456 @ localhost:3306,
    /// base `rakion` seed test/test2), roda de verdade.
    /// </summary>
    public sealed class TwoClientLoginTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task TwoHeadlessClients_LoginConcurrently_ReachAuthenticatedState()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return; // skip suave: banco/stack indisponível
            var server = fixture.Server!;

            await using var alice = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, WorldServerFixture.TcpPort, "alice");
            await using var bob = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, WorldServerFixture.TcpPort, "bob");

            alice.Login("test", "test");
            bob.Login("test2", "test2");

            // Ambos recebem o frame de login sintetizado 0x0C (char-list) do servidor real.
            byte[] aliceLogin = alice.WaitForFirstByte(0x0C, Timeout);
            byte[] bobLogin = bob.WaitForFirstByte(0x0C, Timeout);
            Assert.True(aliceLogin.Length > 4, "0x0C do alice deve ter corpo (char-list sintetizada)");
            Assert.True(bobLogin.Length > 4, "0x0C do bob deve ter corpo (char-list sintetizada)");

            // Ambos recebem também a tabela 0x0D e o desafio GameGuard 0x10.
            alice.WaitForFirstByte(0x0D, Timeout);
            bob.WaitForFirstByte(0x0D, Timeout);

            // Estado do servidor: duas sessões distintas, autenticadas, com as contas certas.
            ClientSession[] sessions = WaitForSessions(server, 2, Timeout);
            Assert.Equal(2, sessions.Length);
            Assert.All(sessions, s => Assert.True(s.Authenticated && s.SlotActive));
            Assert.Contains(sessions, s => string.Equals(s.UserId, "test", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(sessions, s => string.Equals(s.UserId, "test2", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(2, server.CurrentUsers);
            Assert.NotEqual(sessions[0].Slot, sessions[1].Slot);
        }

        [Fact]
        public async Task HeadlessClient_LoginWithWrongPassword_IsRejected()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            var server = fixture.Server!;

            await using var intruder = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, WorldServerFixture.TcpPort, "intruder");
            intruder.Login("test", "senha-errada");

            // Login inválido não deve produzir char-list nem promover a sessão.
            await Task.Delay(1500);
            intruder.DrainReceived();
            Assert.DoesNotContain(intruder.Received, f => f.Length > 0 && f[0] == 0x0C);
            Assert.DoesNotContain(server.Sessions, s => s.Authenticated &&
                string.Equals(s.UserId, "test", StringComparison.OrdinalIgnoreCase));
        }

        private static ClientSession[] WaitForSessions(WorldServer server, int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                ClientSession[] auth = server.Sessions.Where(s => s.Authenticated && s.SlotActive).ToArray();
                if (auth.Length >= count) return auth;
                System.Threading.Thread.Sleep(100);
            }
            return server.Sessions.Where(s => s.Authenticated && s.SlotActive).ToArray();
        }
    }
}
