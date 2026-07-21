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

        [Fact]
        public async Task EmptySlotToggleAfterReady_PreservesReadyAndAllowsStart()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            var (master, joiner, field, joinerSession) = await PrepareRoom(fixture);
            await using (master)
            await using (joiner)
            {
                joiner.SetReady(true);
                WaitUntil(() => field.FindRec(joinerSession)?.LobbyReady == true, Timeout,
                    "joiner não ficou ready");

                master.SetSlotUnlocked(11, false);
                master.SetSlotUnlocked(11, true);
                WaitUntil(() => field.Slots[11].State == 0, Timeout,
                    "slot vazio não foi reaberto");
                Assert.True(field.FindRec(joinerSession)!.LobbyReady);

                master.StartMatch();
                WaitUntil(() => field.MatchId != Guid.Empty, Timeout,
                    "toggle de slot vazio impediu o start");
            }
        }

        [Fact]
        public async Task GraphicFlow_First4BWithout45_PublishesBothPlayerSpawns()
        {
            await using var fixture = await WorldServerFixture.CreateAsync(forceTunneling: true);
            if (!fixture.Available) return;
            var (master, joiner, field, joinerSession) = await PrepareRoom(
                fixture, HeadlessWorldClient.RoomSpec.Deathmatch("e2e-graphic-spawn"));
            await using (master)
            await using (joiner)
            {
                ClientSession masterSession = field.Master!;
                joiner.SetReady(true);
                WaitUntil(() => field.FindRec(joinerSession)?.LobbyReady == true, Timeout,
                    "joiner não ficou ready");
                master.StartMatch();
                WaitUntil(() => field.MatchId != Guid.Empty, Timeout, "partida não armou");

                Assert.Equal((byte)3, field.FindRec(masterSession)!.State);
                Assert.Equal((byte)3, field.FindRec(joinerSession)!.State);
                Assert.Equal(MatchPhase.Pre, field.Phase);

                int masterFramesBeforeReady = master.DrainReceived();
                master.RoundStart();
                WaitUntil(() => field.FindRec(masterSession)?.State == 4, Timeout,
                    "master não confirmou o carregamento");
                Assert.Equal((byte)3, field.FindRec(joinerSession)!.State);
                Thread.Sleep(200);
                master.DrainReceived();
                Assert.DoesNotContain(master.Received.Skip(masterFramesBeforeReady), IsRoundStart);

                master.SpawnField();
                WaitUntil(() => masterSession.PlayerSpawnMatchId == field.MatchId, Timeout,
                    "spawn do master não foi publicado");
                Assert.Equal((byte)4, field.FindRec(masterSession)!.State);

                joiner.RoundStart();
                WaitUntil(() => field.Phase == MatchPhase.Playing, Timeout,
                    "round não iniciou após os dois 0x48");
                Assert.Equal((byte)4, field.FindRec(joinerSession)!.State);
                joiner.SpawnField();
                WaitUntil(() => joinerSession.PlayerSpawnMatchId == field.MatchId, Timeout,
                    "spawn do joiner não foi publicado");
                Assert.Equal((byte)4, field.FindRec(joinerSession)!.State);

                AssertSpawns(master, masterSession.FieldSeat, joinerSession.FieldSeat);
                AssertSpawns(joiner, masterSession.FieldSeat, joinerSession.FieldSeat);
                Thread.Sleep(200);
                master.DrainReceived();
                joiner.DrainReceived();
                Assert.Single(master.Received.Skip(masterFramesBeforeReady), IsRoundStart);
                Assert.Single(joiner.Received, IsRoundStart);

                int masterRoundStarts = master.Received.Count(IsRoundStart);
                int joinerRoundStarts = joiner.Received.Count(IsRoundStart);
                master.RoundStart();
                Thread.Sleep(200);
                master.DrainReceived();
                joiner.DrainReceived();
                Assert.Equal(masterRoundStarts, master.Received.Count(IsRoundStart));
                Assert.Equal(joinerRoundStarts, joiner.Received.Count(IsRoundStart));
            }
        }

        private static bool IsRoundStart(byte[] frame) =>
            frame.Length >= 2 && frame[0] == 0x48 && frame[1] == 0;

        private static bool IsSpawn(byte[] frame) =>
            frame.Length >= 3 && frame[0] == 0x45 && frame[1] == 0;

        private static void AssertSpawns(HeadlessWorldClient client, params byte[] seats)
        {
            byte[] received = seats
                .Select(_ => client.WaitForNext(IsSpawn, Timeout)[2])
                .OrderBy(seat => seat)
                .ToArray();
            Assert.Equal(seats.OrderBy(seat => seat), received);
        }

        private static async Task<(HeadlessWorldClient Master, HeadlessWorldClient Joiner,
            Field Field, ClientSession JoinerSession)> PrepareRoom(
                WorldServerFixture fixture, HeadlessWorldClient.RoomSpec? room = null)
        {
            WorldServer server = fixture.Server!;
            var master = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "slot-master");
            var joiner = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "slot-joiner");
            master.Login("test", "test");
            joiner.Login("test2", "test2");
            master.WaitForFirstByte(0x0C, Timeout);
            joiner.WaitForFirstByte(0x0C, Timeout);
            master.SelectCharacter(1);
            joiner.SelectCharacter(9001);
            ClientSession masterSession = WaitForSession(server, "test",
                session => session.ActiveCharId > 0 && session.Status == UserStatus.FieldLobby,
                Timeout);
            ClientSession joinerSession = WaitForSession(server, "test2",
                session => session.ActiveCharId > 0 && session.Status == UserStatus.FieldLobby,
                Timeout);
            master.CreateRoom(room ?? HeadlessWorldClient.RoomSpec.Golem("e2e-slot-ready"));
            WaitUntil(() => masterSession.FieldId >= 0, Timeout, "sala não criada");
            Field field = server.GetField(masterSession.FieldId)!;
            joiner.JoinRoom((ushort)field.Id);
            WaitUntil(() => joinerSession.FieldId == field.Id, Timeout, "joiner não entrou");
            return (master, joiner, field, joinerSession);
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
