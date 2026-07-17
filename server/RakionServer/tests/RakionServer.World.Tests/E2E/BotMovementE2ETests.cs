using System;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>
    /// Valida no fio o MOVIMENTO do bot: um humano cria uma sala Golem, adiciona um bot (time oposto),
    /// faz o handshake UDP e a partida entra em jogo. O motor sintetiza o 0x030A do bot e injeta no
    /// socket do humano — o cliente recebe o peer sintético se movendo. Prova o núcleo funcional do bot
    /// reconstruído do RE (sem cliente gráfico). Teto conhecido: sem o número cosmético HIT×N nativo.
    /// </summary>
    [Collection("E2E")]
    public sealed class BotMovementE2ETests
    {
        [Fact]
        public async Task Bot_InPlayingMatch_SynthesizesMovementToHuman()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return; // skip suave
            var server = fixture.Server!;

            await using var human = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "human");

            human.Login("test", "test");
            human.WaitForFirstByte(0x0C, JourneyHelper.Timeout);
            human.SelectCharacter(1);
            ClientSession hs = JourneyHelper.WaitForSession(server, "test",
                s => s.ActiveCharId > 0 && s.Status == UserStatus.FieldLobby);

            human.CreateRoom(HeadlessWorldClient.RoomSpec.Golem("e2e-bot"));
            JourneyHelper.WaitUntil(() => hs.FieldId >= 0 && server.GetField(hs.FieldId) != null, "sala não criada");
            Field field = server.GetField(hs.FieldId)!;

            // Adiciona um bot (time oposto ao host) direto pelo serviço de domínio.
            var add = server.Bots.AddBotToField(field, hs, BotDifficulty.Hard);
            Assert.True(add.Ok, add.Message);
            Assert.InRange(add.Seat, 10, 19);

            // Handshake UDP do humano + posição rastreável (um 0x030A do humano).
            human.OpenUdp();
            human.UdpHandshake(fixture.UdpPort2, hs.Slot, hs.UdpKey);
            JourneyHelper.WaitUntil(() => hs.UdpEndpoint != null, "endpoint UDP não autenticado");
            human.WaitForUdp(p => p.Length == 12 && p[0] == 0x01 && p[1] == 0x02, JourneyHelper.Timeout);
            human.SendMove(fixture.UdpPort2, hs.FieldSeat, 500, 0, 500); // posição do humano p/ o bot mirar

            // Coloca o field EM JOGO (State 2) — o game clock passa a tickar os bots.
            lock (field.SyncRoot) { field.State = 2; field.Phase = MatchPhase.Playing; }

            // O humano recebe o 0x030A SINTETIZADO do bot (origem = assento do bot, no time 1).
            byte[] botMove = human.WaitForUdp(
                p => p.Length == 26 && p[0] == 0x0a && p[1] == 0x03 && p[6] >= 10, JourneyHelper.Timeout);

            Assert.Equal((byte)add.Seat, botMove[6]);   // assento de origem = o bot
            Assert.Equal(0x030a, BitConverter.ToUInt16(botMove, 0));
        }
    }
}
