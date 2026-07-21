using System;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    [Collection("E2E")]
    public sealed class TwoClientInvitationTests
    {
        [Fact]
        public async Task RoomMaster_CanInvitePlayerFromGameList()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            WorldServer server = fixture.Server!;

            await using var master = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "master");
            await using var target = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "target");

            master.Login("test", "test");
            target.Login("test2", "test2");
            master.WaitForFirstByte(0x0C, JourneyHelper.Timeout);
            target.WaitForFirstByte(0x0C, JourneyHelper.Timeout);
            master.SelectCharacter(1);
            target.SelectCharacter(9001);

            ClientSession masterSession = JourneyHelper.WaitForSession(server, "test",
                session => session.ActiveCharId > 0 && session.Status == UserStatus.FieldLobby);
            ClientSession targetSession = JourneyHelper.WaitForSession(server, "test2",
                session => session.ActiveCharId > 0 && session.Status == UserStatus.FieldLobby);

            master.CreateGolemRoom("invite-e2e");
            JourneyHelper.WaitUntil(() => masterSession.FieldId > 0, "sala não criada");
            master.Invite(targetSession.Slot);

            byte[] invitation = target.WaitForNextFirstByte(0x72, JourneyHelper.Timeout);

            Assert.Equal((byte)0x72, invitation[0]);
            Assert.Equal((byte)0x00, invitation[1]);
            Assert.True(masterSession.Connected);
            Assert.True(targetSession.Connected);
            Assert.Equal(UserStatus.FieldLobby, masterSession.Status);
            Assert.Equal(-1, targetSession.FieldId);
        }
    }
}
