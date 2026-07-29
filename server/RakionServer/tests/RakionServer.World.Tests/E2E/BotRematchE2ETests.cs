using System;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    [Collection("E2E")]
    public sealed class BotRematchE2ETests
    {
        [Fact]
        public async Task ExitingMatchWithBot_AllowsImmediateRematchAndRoomListing()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            WorldServer server = fixture.Server!;
            await using var master = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "bot-rematch-master");
            await using var joiner = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "bot-rematch-joiner");

            var (masterSession, joinerSession, field) = JourneyHelper.DriveToUdpReadyRoom(
                server, master, joiner,
                HeadlessWorldClient.RoomSpec.Deathmatch("bot-rematch"), fixture.UdpPort2);
            BotManager.AddBotResult added = TestBotAdmission.Add(
                server.Bots, field, masterSession, BotDifficulty.Normal);
            Assert.True(added.Ok, added.Message);
            MatchRecordSnapshot masterBefore = await MatchRecordFixture.ReadAsync(
                fixture.DbConnectionString, 1);
            MatchRecordSnapshot joinerBefore = await MatchRecordFixture.ReadAsync(
                fixture.DbConnectionString, 9001);

            try
            {
                joiner.SetReady(true);
                JourneyHelper.WaitUntil(() => field.FindRec(joinerSession)?.LobbyReady == true,
                    "joiner não ficou ready na primeira partida");
                master.StartMatch();
                JourneyHelper.WaitUntil(() => masterSession.Status == UserStatus.InField &&
                    joinerSession.Status == UserStatus.InField, "primeira partida não iniciou");
                Guid firstMatchId = field.MatchId;

                master.SpawnField();
                joiner.SpawnField();
                master.RoundStart();
                joiner.RoundStart();
                JourneyHelper.WaitUntil(() => field.FindRec(masterSession)?.Playing == true &&
                    field.FindRec(joinerSession)?.Playing == true, "players não entraram no stage");

                master.ExitFieldGame();
                joiner.ExitFieldGame();
                JourneyHelper.WaitUntil(() => field.State == 1 &&
                    masterSession.Status == UserStatus.FieldLobby &&
                    joinerSession.Status == UserStatus.FieldLobby, "field não voltou ao game room");

                PlayerRec botRecord = field.Slots[added.Seat];
                Assert.Equal((byte)2, botRecord.State);
                Assert.True(botRecord.Bot!.Alive);
                var available = new RoomListQuery(10, 0, true, 1 << 2, false);
                Assert.Contains(server.ListJoinableFields(joinerSession, available),
                    room => room.FieldId == field.Id);

                joiner.SetReady(true);
                JourneyHelper.WaitUntil(() => field.FindRec(joinerSession)?.LobbyReady == true,
                    "joiner não ficou ready no rematch");
                master.StartMatch();
                JourneyHelper.WaitUntil(() => field.MatchId != firstMatchId && field.State == 2 &&
                    masterSession.Status == UserStatus.InField &&
                    joinerSession.Status == UserStatus.InField, "rematch não iniciou");
            }
            finally
            {
                await MatchRecordFixture.RestoreAsync(
                    fixture.DbConnectionString, masterBefore, joinerBefore);
            }
        }
    }
}
