using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>
    /// Valida o SETTLEMENT persistido de uma partida PvP com dois clientes headless que
    /// vieram pelo fio: TeamDeath, times opostos, partida armada; ao encerrar com o time 0
    /// vencedor, o motor da partida vivo chama `SettleMatchAsync` e grava win/lose no
    /// `characterinfo` real. Fecha o elo motor-de-partida → banco para sessões de rede.
    /// </summary>
    [Collection("E2E")]
    public sealed class TwoClientSettlementTests
    {
        [Fact]
        public async Task PvpMatchEnd_PersistsWinLoseToDatabase()
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
            master.WaitForFirstByte(0x0C, JourneyHelper.Timeout);
            joiner.WaitForFirstByte(0x0C, JourneyHelper.Timeout);

            master.SelectCharacter(1);
            joiner.SelectCharacter(9001);
            ClientSession ms = JourneyHelper.WaitForSession(server, "test",
                s => s.ActiveCharId > 0 && s.Status == UserStatus.FieldLobby);
            ClientSession js = JourneyHelper.WaitForSession(server, "test2",
                s => s.ActiveCharId > 0 && s.Status == UserStatus.FieldLobby);

            master.CreateRoom(HeadlessWorldClient.RoomSpec.TeamDeath("e2e-settle"));
            JourneyHelper.WaitUntil(() => ms.FieldId >= 0 && server.GetField(ms.FieldId) != null, "sala não criada");
            int fieldId = ms.FieldId;
            Field field = server.GetField(fieldId)!;

            joiner.JoinRoom((ushort)fieldId);
            JourneyHelper.WaitUntil(() => js.FieldId == fieldId, "joiner não entrou");

            // Joiner vai para o time oposto (assento 10..19) e fica ready; master inicia.
            joiner.ChangeTeam();
            JourneyHelper.WaitUntil(() => js.FieldSeat >= 10, "joiner não trocou de time");
            joiner.SetReady(true);
            JourneyHelper.WaitUntil(() => field.FindRec(js)?.LobbyReady == true, "joiner não ficou ready");
            master.StartMatch();
            JourneyHelper.WaitUntil(() => field.MatchId != Guid.Empty, "partida não foi armada");

            (int win1, int lose1) = await ReadWinLoseAsync(fixture.DbConnectionString, 1);
            (int win2, int lose2) = await ReadWinLoseAsync(fixture.DbConnectionString, 9001);

            // Encerra a partida com o time 0 (master) vencedor; o motor da partida vivo liquida.
            lock (field.SyncRoot)
            {
                field.Wins0 = 1;
                field.Wins1 = 0;
                field.EndMatch(0);
            }
            JourneyHelper.WaitUntil(() => field.Settled, "settlement não concluiu");
            await Task.Delay(300); // deixa o UPDATE do DB confirmar

            (int win1b, int lose1b) = await ReadWinLoseAsync(fixture.DbConnectionString, 1);
            (int win2b, int lose2b) = await ReadWinLoseAsync(fixture.DbConnectionString, 9001);

            Assert.Equal(win1 + 1, win1b);   // GoHeroi (time 0) venceu
            Assert.Equal(lose1, lose1b);
            Assert.Equal(lose2 + 1, lose2b); // ProbeTwo (time 1) perdeu
            Assert.Equal(win2, win2b);
        }

        private static async Task<(int win, int lose)> ReadWinLoseAsync(string conn, int characterId)
        {
            await using var connection = new MySqlConnection(conn);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT win, lose FROM characterinfo WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", characterId);
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), $"characterinfo id={characterId} ausente");
            return (reader.GetInt32(0), reader.GetInt32(1));
        }
    }
}
