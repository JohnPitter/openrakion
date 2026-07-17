using System.Text;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>
    /// Valida o chat de canal (0x22) entre dois clientes headless no mesmo canal-lobby:
    /// um envia, o outro recebe o broadcast (frame `0x22` + slot + texto). Fecha o path
    /// social no fio, incluindo a entrega ao próprio remetente.
    /// </summary>
    [Collection("E2E")]
    public sealed class TwoClientChatTests
    {
        [Fact]
        public async Task ChannelChat_BroadcastsToOtherClientInSameChannel()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return; // skip suave
            var server = fixture.Server!;

            await using var speaker = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "speaker");
            await using var listener = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "listener");

            speaker.Login("test", "test");
            listener.Login("test2", "test2");
            speaker.WaitForFirstByte(0x0C, JourneyHelper.Timeout);
            listener.WaitForFirstByte(0x0C, JourneyHelper.Timeout);

            speaker.SelectCharacter(1);
            listener.SelectCharacter(9001);
            JourneyHelper.WaitForSession(server, "test",
                s => s.ActiveCharId > 0 && s.Status == UserStatus.FieldLobby);
            JourneyHelper.WaitForSession(server, "test2",
                s => s.ActiveCharId > 0 && s.Status == UserStatus.FieldLobby);

            const string message = "GoHeroi : ola-e2e-chat";
            speaker.SendChannelChat(message);

            // Ambos (remetente e ouvinte) recebem o frame de chat 0x22 com o texto.
            byte[] heard = listener.WaitFor(ContainsChat, JourneyHelper.Timeout);
            byte[] echo = speaker.WaitFor(ContainsChat, JourneyHelper.Timeout);
            Assert.Contains("ola-e2e-chat", Ascii(heard));
            Assert.Contains("ola-e2e-chat", Ascii(echo));

            static bool ContainsChat(byte[] frame) =>
                frame.Length > 3 && frame[0] == 0x22 && frame[1] == 0x00 &&
                Ascii(frame).Contains("ola-e2e-chat");

            static string Ascii(byte[] frame) => Encoding.ASCII.GetString(frame);
        }
    }
}
