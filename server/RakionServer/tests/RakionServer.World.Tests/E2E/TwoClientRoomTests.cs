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
    /// Validação dinâmica da jornada de sala com dois clientes headless reais: login →
    /// char-select → um cria a sala (Golem War) → o outro entra. Prova, no fio, que o
    /// servidor porta criação de sala competitiva, papel de master, join do segundo
    /// jogador e a coabitação dos dois no mesmo <see cref="Field"/> com assentos
    /// distintos — sem cliente gráfico.
    /// </summary>
    [Collection("E2E")]
    public sealed class TwoClientRoomTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task TwoHeadlessClients_CreateAndJoinGolemRoom_ShareFieldWithDistinctSeats()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return; // skip suave
            var server = fixture.Server!;

            await using var master = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "master");
            await using var joiner = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "joiner");

            // Login + char-select (test→GoHeroi #1, test2→ProbeTwo #9001).
            master.Login("test", "test");
            joiner.Login("test2", "test2");
            master.WaitForFirstByte(0x0C, Timeout);
            joiner.WaitForFirstByte(0x0C, Timeout);

            master.SelectCharacter(1);
            joiner.SelectCharacter(9001);
            // FieldLobby == LoggedIn == 2, então o gate real é o char selecionado (ActiveCharId>0).
            ClientSession masterSession = WaitForSession(server, "test",
                s => s.ActiveCharId > 0 && s.Status == UserStatus.FieldLobby, Timeout);
            ClientSession joinerSession = WaitForSession(server, "test2",
                s => s.ActiveCharId > 0 && s.Status == UserStatus.FieldLobby, Timeout);

            // Master cria a sala Golem; lê o field criado no estado do servidor.
            master.CreateGolemRoom("e2e-golem");
            WaitUntil(() => masterSession.FieldId >= 0 && server.GetField(masterSession.FieldId) != null, Timeout, "sala não criada");
            int fieldId = masterSession.FieldId;
            Field field = server.GetField(fieldId)!;
            Assert.NotNull(field);
            Assert.Equal((byte)GameMode.Golem, field.Mode);
            Assert.Same(masterSession, field.Master);

            // Joiner entra pela sala anunciada.
            joiner.JoinRoom((ushort)fieldId);
            WaitUntil(() => joinerSession.FieldId == fieldId, Timeout, "joiner não entrou na sala");

            // Coabitação no mesmo field, assentos distintos, master preservado.
            Assert.Equal(fieldId, masterSession.FieldId);
            Assert.Equal(fieldId, joinerSession.FieldId);
            Assert.NotEqual(masterSession.FieldSeat, joinerSession.FieldSeat);
            Assert.Same(masterSession, field.Master);

            int occupied = field.Slots.Count(rec => rec.Session != null);
            Assert.Equal(2, occupied);
            Assert.Contains(field.Slots, rec => rec.Session == masterSession);
            Assert.Contains(field.Slots, rec => rec.Session == joinerSession);

            // O cliente gráfico solicita o peer UDP ainda na game room, antes do 0x4B.
            Assert.Equal(UserStatus.FieldLobby, masterSession.Status);
            Assert.Equal(UserStatus.FieldLobby, joinerSession.Status);
            master.Send(0x62, new[] { joinerSession.FieldSeat });
            byte[] bootstrap = joiner.WaitForNext(
                frame => frame.Length >= 3 && frame[0] == 0x62 && frame[1] == 0x00,
                Timeout);
            Assert.Equal(masterSession.FieldSeat, bootstrap[2]);
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
