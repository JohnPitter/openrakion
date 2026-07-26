using System;
using System.IO;
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
        public async Task FieldLobbyCommand_FailsClosedWhenBotEngineIsDisabled()
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
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            Assert.Equal(UserStatus.FieldLobby, session.Status);
            Assert.Equal(field.Id, session.FieldId);
            Assert.Equal(0, field.BotCount);
        }

        [Fact]
        [Trait("Requires", "BotEngineFixture")]
        public async Task FieldLobbyCommand_AddsBotAfterNativeHostConfirmation()
        {
            string? hostPath = Environment.GetEnvironmentVariable(
                "RAKION_BOT_ENGINE_HOST");
            string? clientRoot = Environment.GetEnvironmentVariable(
                "RAKION_BOT_ENGINE_CLIENT_ROOT");
            if (string.IsNullOrWhiteSpace(hostPath) ||
                string.IsNullOrWhiteSpace(clientRoot))
                return;
            Assert.True(File.Exists(hostPath), hostPath);
            Assert.True(Directory.Exists(clientRoot), clientRoot);

            await using var fixture = await WorldServerFixture.CreateAsync(
                configure: config =>
                {
                    config.BotEngine.Enabled = true;
                    config.BotEngine.HostPath = hostPath;
                    config.BotEngine.ClientRoot = clientRoot;
                });
            if (!fixture.Available) return;
            WorldServer server = fixture.Server!;
            await using var human = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "native-add-bot");
            human.Login("test", "test");
            human.WaitForFirstByte(0x0C, JourneyHelper.Timeout);
            human.SelectCharacter(1);
            ClientSession session = JourneyHelper.WaitForSession(server, "test",
                value => value.ActiveCharId > 0 && value.Status == UserStatus.FieldLobby);
            human.CreateGolemRoom("native-add-bot");
            JourneyHelper.WaitUntil(
                () => session.FieldId >= 0 && server.GetField(session.FieldId) != null,
                "sala não criada");
            Field field = server.GetField(session.FieldId)!;

            human.SendFieldChat("GoHeroi : /addbot");
            JourneyHelper.WaitUntil(
                () => field.BotSlots.Any(record => record.Bot!.EngineAttached),
                "Host nativo não confirmou o bot",
                TimeSpan.FromSeconds(40));

            Assert.Equal(UserStatus.FieldLobby, session.Status);
            Assert.Single(field.BotSlots);
        }
    }
}
