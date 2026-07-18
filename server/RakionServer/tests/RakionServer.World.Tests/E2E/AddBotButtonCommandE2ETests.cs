using System.Linq;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    [Collection("E2E")]
    public sealed class AddBotButtonCommandE2ETests
    {
        [Fact]
        public async Task FieldLobbyCommand_FromOriginalAddBotButton_AddsBotWithoutDisconnect()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            WorldServer server = fixture.Server!;

            await using var human = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "add-bot-button");
            human.Login("test", "test");
            human.WaitForFirstByte(0x0C, JourneyHelper.Timeout);
            human.SelectCharacter(1);
            ClientSession session = JourneyHelper.WaitForSession(server, "test",
                value => value.ActiveCharId > 0 && value.Status == UserStatus.FieldLobby);

            human.CreateGolemRoom("add-bot-button");
            JourneyHelper.WaitUntil(
                () => session.FieldId >= 0 && server.GetField(session.FieldId) != null,
                "sala não criada");
            Field field = server.GetField(session.FieldId)!;

            human.SendFieldChat("GoHeroi : /addbot");
            JourneyHelper.WaitUntil(
                () => field.Slots.Count(record => record.Bot != null) == 1,
                "botão Add Bot não adicionou o bot");

            Assert.Equal(UserStatus.FieldLobby, session.Status);
            Assert.Equal(field.Id, session.FieldId);
            Assert.Single(field.Slots, record => record.Bot != null);
        }
    }
}
