using System;
using System.Threading.Tasks;
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

            MatchRecordSnapshot masterBefore = await MatchRecordFixture.ReadAsync(
                fixture.DbConnectionString, 1);
            MatchRecordSnapshot joinerBefore = await MatchRecordFixture.ReadAsync(
                fixture.DbConnectionString, 9001);

            try
            {
                // Encerra a partida com o time 0 (master) vencedor; o motor da partida vivo liquida.
                lock (field.SyncRoot)
                {
                    field.Wins0 = 1;
                    field.Wins1 = 0;
                    field.EndMatch(0);
                }
                JourneyHelper.WaitUntil(() => field.Settled, "settlement não concluiu");
                await Task.Delay(300); // deixa o UPDATE do DB confirmar

                MatchRecordSnapshot masterAfter = await MatchRecordFixture.ReadAsync(
                    fixture.DbConnectionString, 1);
                MatchRecordSnapshot joinerAfter = await MatchRecordFixture.ReadAsync(
                    fixture.DbConnectionString, 9001);

                Assert.Equal(masterBefore.Win + 1, masterAfter.Win);
                Assert.Equal(masterBefore.Lose, masterAfter.Lose);
                Assert.Equal(joinerBefore.Lose + 1, joinerAfter.Lose);
                Assert.Equal(joinerBefore.Win, joinerAfter.Win);
            }
            finally
            {
                await MatchRecordFixture.RestoreAsync(
                    fixture.DbConnectionString, masterBefore, joinerBefore);
            }
        }
    }
}
