using System.Threading.Tasks;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    [Collection("E2E")]
    public sealed class SoloStageExitTests
    {
        [Fact]
        public async Task LeavingRunningStageThenRoomKeepsSessionConnected()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            WorldServer server = fixture.Server!;

            await using var player = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "solo-exit");
            player.Login("test2", "test2");
            player.WaitForFirstByte(0x0C, JourneyHelper.Timeout);
            player.SelectCharacter(9001);
            ClientSession session = JourneyHelper.WaitForSession(server, "test2",
                value => value.ActiveCharId > 0 && value.Status == UserStatus.FieldLobby);

            player.CreateRoom(new HeadlessWorldClient.RoomSpec(
                "e2e-stage-exit", 1, 0, 1, 432, 0, 1, 99));
            JourneyHelper.WaitUntil(() => session.FieldId >= 0, "stage não foi criado");
            player.StartMatch();
            JourneyHelper.WaitUntil(() => session.Status == UserStatus.InField,
                "stage não iniciou");
            player.SpawnField();

            player.ExitFieldGame();
            JourneyHelper.WaitUntil(() => session.Status == UserStatus.FieldLobby,
                "saída da partida não retornou ao game room");
            Assert.True(session.Connected);

            player.ExitRoom();
            JourneyHelper.WaitUntil(() => session.FieldId < 0,
                "saída do game room não retornou à game list");
            Assert.True(session.Connected);
            Assert.Equal(UserStatus.FieldLobby, session.Status);
        }
    }
}
