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
    /// Fecha a jornada de sala com o START da partida por dois clientes headless reais:
    /// login → char-select → criar sala Golem → entrar → joiner READY (0x3d) → master
    /// START (0x43). Valida no fio que o servidor arma a partida (fase Pre + deadline de
    /// engajamento) e promove ambos os assentos ao estado de combatente. É a fronteira
    /// lobby→field exercitada ponta a ponta, sem cliente gráfico.
    /// </summary>
    [Collection("E2E")]
    public sealed class TwoClientMatchStartTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task TwoHeadlessClients_ReadyAndStart_ArmsMatchAndPromotesBothSeats()
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

            master.CreateGolemRoom("e2e-start");
            WaitUntil(() => masterSession.FieldId >= 0 && server.GetField(masterSession.FieldId) != null, Timeout, "sala não criada");
            int fieldId = masterSession.FieldId;
            Field field = server.GetField(fieldId)!;

            joiner.JoinRoom((ushort)fieldId);
            WaitUntil(() => joinerSession.FieldId == fieldId, Timeout, "joiner não entrou");

            // Joiner marca ready; master só consegue iniciar quando o não-master está pronto.
            joiner.SetReady(true);
            WaitUntil(() => field.FindRec(joinerSession)?.LobbyReady == true, Timeout, "joiner não ficou ready");

            master.StartMatch();

            // A partida é armada: fase Pre, deadline de engajamento (~40s) e ambos os
            // assentos promovidos a combatente (State==3).
            WaitUntil(() => field.Phase == MatchPhase.Pre && field.MatchId != Guid.Empty, Timeout,
                "partida não foi armada");
            Assert.Equal(MatchPhase.Pre, field.Phase);
            Assert.NotEqual(Guid.Empty, field.MatchId);
            Assert.True(field.DeadlineMs > Environment.TickCount64,
                "deadline de engajamento deve estar no futuro");

            byte masterState = field.FindRec(masterSession)!.State;
            byte joinerState = field.FindRec(joinerSession)!.State;
            Assert.Equal((byte)3, masterState);
            Assert.Equal((byte)3, joinerState);
            Assert.False(field.Settled);
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
