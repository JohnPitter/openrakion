using System;
using System.Linq;
using System.Threading;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>Passos reusáveis da jornada headless (login→sala→field→UDP) para os testes E2E.</summary>
    internal static class JourneyHelper
    {
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        public static ClientSession WaitForSession(
            WorldServer server, string account, Func<ClientSession, bool> ready)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < deadline)
            {
                ClientSession? s = server.Sessions.FirstOrDefault(
                    x => string.Equals(x.UserId, account, StringComparison.OrdinalIgnoreCase));
                if (s != null && ready(s)) return s;
                Thread.Sleep(100);
            }
            throw new TimeoutException($"sessão '{account}' não atingiu o estado esperado em {Timeout.TotalSeconds:0.#}s");
        }

        public static void WaitUntil(Func<bool> condition, string message)
        {
            WaitUntil(condition, message, Timeout);
        }

        public static void WaitUntil(
            Func<bool> condition,
            string message,
            TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return;
                Thread.Sleep(100);
            }
            throw new TimeoutException(message + $" (timeout {timeout.TotalSeconds:0.#}s)");
        }

        /// <summary>Leva os dois clientes até coabitarem um field competitivo (mode dado) com UDP
        /// autenticado. Retorna as sessões e o field. Não inicia a partida.</summary>
        public static (ClientSession master, ClientSession joiner, Field field) DriveToUdpReadyRoom(
            WorldServer server, HeadlessWorldClient master, HeadlessWorldClient joiner,
            HeadlessWorldClient.RoomSpec spec, int udpPort)
        {
            master.Login("test", "test");
            joiner.Login("test2", "test2");
            master.WaitForFirstByte(0x0C, Timeout);
            joiner.WaitForFirstByte(0x0C, Timeout);

            master.SelectCharacter(1);
            joiner.SelectCharacter(9001);
            ClientSession ms = WaitForSession(server, "test",
                s => s.ActiveCharId > 0 && s.Status == UserStatus.FieldLobby);
            ClientSession js = WaitForSession(server, "test2",
                s => s.ActiveCharId > 0 && s.Status == UserStatus.FieldLobby);

            master.CreateRoom(spec);
            WaitUntil(() => ms.FieldId >= 0 && server.GetField(ms.FieldId) != null, "sala não criada");
            int fieldId = ms.FieldId;
            Field field = server.GetField(fieldId)!;

            joiner.JoinRoom((ushort)fieldId);
            WaitUntil(() => js.FieldId == fieldId, "joiner não entrou");

            master.OpenUdp();
            joiner.OpenUdp();
            master.UdpHandshake(udpPort, ms.Slot, ms.UdpKey);
            joiner.UdpHandshake(udpPort, js.Slot, js.UdpKey);
            WaitUntil(() => ms.UdpEndpoint != null && js.UdpEndpoint != null,
                "endpoints UDP não autenticados");
            master.WaitForUdp(p => p.Length == 12 && p[0] == 0x01 && p[1] == 0x02, Timeout);
            joiner.WaitForUdp(p => p.Length == 12 && p[0] == 0x01 && p[1] == 0x02, Timeout);

            return (ms, js, field);
        }

        /// <summary>Leva os dois até a partida ARMADA: login→char-select→criar sala→join→ready→start.
        /// Não usa UDP. Retorna sessões e field (com MatchId setado).</summary>
        public static (ClientSession master, ClientSession joiner, Field field) DriveToArmedMatch(
            WorldServer server, HeadlessWorldClient master, HeadlessWorldClient joiner,
            HeadlessWorldClient.RoomSpec spec)
        {
            var journey = DriveToJoinedRoom(server, master, joiner, spec);
            joiner.SetReady(true);
            WaitUntil(() => journey.field.FindRec(journey.joiner)?.LobbyReady == true,
                "joiner não ficou ready");
            master.StartMatch();
            WaitUntil(() => journey.field.MatchId != Guid.Empty, "partida não foi armada");
            return journey;
        }

        /// <summary>Leva dois jogadores em times opostos até uma partida armada.</summary>
        public static (ClientSession master, ClientSession joiner, Field field) DriveToArmedOpposingTeamsMatch(
            WorldServer server, HeadlessWorldClient master, HeadlessWorldClient joiner,
            HeadlessWorldClient.RoomSpec spec)
        {
            var journey = DriveToJoinedRoom(server, master, joiner, spec);
            joiner.ChangeTeam();
            WaitUntil(() => journey.joiner.FieldSeat >= 10, "joiner não trocou de time");
            joiner.SetReady(true);
            WaitUntil(() => journey.field.FindRec(journey.joiner)?.LobbyReady == true,
                "joiner não ficou ready");
            master.StartMatch();
            WaitUntil(() => journey.field.MatchId != Guid.Empty, "partida não foi armada");
            return journey;
        }

        private static (ClientSession master, ClientSession joiner, Field field) DriveToJoinedRoom(
            WorldServer server, HeadlessWorldClient master, HeadlessWorldClient joiner,
            HeadlessWorldClient.RoomSpec spec)
        {
            master.Login("test", "test");
            joiner.Login("test2", "test2");
            master.WaitForFirstByte(0x0C, Timeout);
            joiner.WaitForFirstByte(0x0C, Timeout);

            master.SelectCharacter(1);
            joiner.SelectCharacter(9001);
            ClientSession ms = WaitForSession(server, "test",
                s => s.ActiveCharId > 0 && s.Status == UserStatus.FieldLobby);
            ClientSession js = WaitForSession(server, "test2",
                s => s.ActiveCharId > 0 && s.Status == UserStatus.FieldLobby);

            master.CreateRoom(spec);
            WaitUntil(() => ms.FieldId >= 0 && server.GetField(ms.FieldId) != null, "sala não criada");
            int fieldId = ms.FieldId;
            Field field = server.GetField(fieldId)!;

            joiner.JoinRoom((ushort)fieldId);
            WaitUntil(() => js.FieldId == fieldId, "joiner não entrou");
            return (ms, js, field);
        }
    }
}
