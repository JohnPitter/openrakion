using System;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>
    /// Validação do bot NO STAGE, no fio: (1) o bot persegue um humano que se move — a posição do bot
    /// converge para a do humano ao longo dos ticks; (2) convivência com dois humanos — o bot mira o
    /// inimigo e ambos recebem o movimento, sem sequestro do P2P humano; (3) combate — o humano ataca
    /// perto do bot e o bot MORRE server-side, com o 0x4f transmitido aos humanos.
    /// </summary>
    [Collection("E2E")]
    public sealed class BotStageValidationTests
    {
        [Fact]
        public async Task Bot_ChasesMovingHuman_PositionConverges()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            var server = fixture.Server!;

            await using var human = await HeadlessWorldClient.ConnectAsync(WorldServerFixture.Host, fixture.TcpPort, "h");
            var (hs, field, bot) = await SetupBotMatchAsync(server, human, fixture, BotDifficulty.Hard);

            // Humano num ponto distante; o bot deve se aproximar tick a tick.
            var humanPos = new BotVector(4000, 0, 4000);
            human.SendMove(fixture.UdpPort2, hs.FieldSeat, (short)humanPos.X, 0, (short)humanPos.Z);

            float startDist = bot.Position.HorizontalDistanceTo(humanPos);
            // Reafirma a posição do humano por ~1.5s enquanto o bot persegue.
            for (int i = 0; i < 10; i++)
            {
                human.SendMove(fixture.UdpPort2, hs.FieldSeat, (short)humanPos.X, 0, (short)humanPos.Z);
                Thread.Sleep(150);
            }
            float endDist = bot.Position.HorizontalDistanceTo(humanPos);

            Assert.True(endDist < startDist, $"bot deve se aproximar do humano (de {startDist:0} para {endDist:0})");
        }

        [Fact]
        public async Task Bot_WithTwoHumans_TargetsEnemyAndBothReceiveMovement()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            var server = fixture.Server!;

            await using var master = await HeadlessWorldClient.ConnectAsync(WorldServerFixture.Host, fixture.TcpPort, "m");
            await using var joiner = await HeadlessWorldClient.ConnectAsync(WorldServerFixture.Host, fixture.TcpPort, "j");

            var (ms, js, field) = JourneyHelper.DriveToUdpReadyRoom(
                server, master, joiner, HeadlessWorldClient.RoomSpec.TeamDeath("bot-2h"), fixture.UdpPort2);
            field.Slots[ms.FieldSeat].UsesTunneling = false;
            field.Slots[js.FieldSeat].UsesTunneling = false;

            // joiner vai para o time 1; bot entra no time oposto ao master (time 1 também) -> mira o joiner?
            // O bot entra no time OPOSTO ao HOST (master, time 0) => time 1. Alvo do bot = inimigos (time 0) = master.
            var add = server.Bots.AddBotToField(field, ms, BotDifficulty.Normal);
            Assert.True(add.Ok, add.Message);

            lock (field.SyncRoot) { field.State = 2; field.Phase = MatchPhase.Playing; }
            master.SendMove(fixture.UdpPort2, ms.FieldSeat, 1000, 0, 1000);
            joiner.SendMove(fixture.UdpPort2, js.FieldSeat, -1000, 0, -1000);

            // Ambos os humanos recebem o 0x030A do bot (origem = assento do bot).
            byte[] atMaster = master.WaitForUdp(p => p.Length == 26 && p[0] == 0x0a && p[1] == 0x03 && p[6] == add.Seat, JourneyHelper.Timeout);
            byte[] atJoiner = joiner.WaitForUdp(p => p.Length == 26 && p[0] == 0x0a && p[1] == 0x03 && p[6] == add.Seat, JourneyHelper.Timeout);
            Assert.Equal((byte)add.Seat, atMaster[6]);
            Assert.Equal((byte)add.Seat, atJoiner[6]);

            // O bot (time 1) mira o inimigo (time 0 = master).
            Assert.Equal(ms.FieldSeat, field.Slots[add.Seat].Bot!.TargetSeat);
        }

        [Fact]
        public async Task Bot_KilledByHumanAttack_ReportsDeathToField()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            var server = fixture.Server!;

            await using var human = await HeadlessWorldClient.ConnectAsync(WorldServerFixture.Host, fixture.TcpPort, "k");
            var (hs, field, bot) = await SetupBotMatchAsync(server, human, fixture, BotDifficulty.Normal);

            // Coloca humano e bot no mesmo ponto (dentro do alcance de melee).
            var spot = new BotVector(1000, 0, 1000);
            lock (field.SyncRoot) { field.Slots[hs.FieldSeat].Position = spot; field.Slots[bot.Seat].Position = spot; bot.Position = spot; }
            human.SendMove(fixture.UdpPort2, hs.FieldSeat, (short)spot.X, 0, (short)spot.Z);

            int healthBefore = bot.Health;
            human.SendBotTelemetryAttack(fixture.UdpPort2, hs.FieldSeat, kind: 1);
            Thread.Sleep(100);
            Assert.Equal(healthBefore, bot.Health);

            human.SendConfirmedBotHit(fixture.UdpPort2, bot.Seat);
            byte[] hitReaction = human.WaitForUdp(
                packet => IsBotDamage(packet, bot.Seat), JourneyHelper.Timeout);
            Assert.Equal((byte)bot.Seat, hitReaction[6]);
            Assert.True(bot.Health < healthBefore, "primeiro golpe deve reduzir o HP do bot");

            // Continua golpeando até o bot morrer (HP base ~ level*10+100; dano 34/golpe).
            byte[]? death = null;
            for (int i = 1; i < 20 && death == null; i++)
            {
                human.SendConfirmedBotHit(fixture.UdpPort2, bot.Seat);
                try { death = human.WaitFor(Frame4f, TimeSpan.FromMilliseconds(400)); }
                catch (TimeoutException) { }
            }

            Assert.False(bot.Alive, "bot deve estar morto após os golpes");
            Assert.NotNull(death);
            Assert.Equal((byte)bot.Seat, death![2]);
        }

        [Fact]
        public async Task Bot_WithForcedTunneling_PublishesMovementAndDamageOverTcp()
        {
            await using var fixture = await WorldServerFixture.CreateAsync(forceTunneling: true);
            if (!fixture.Available) return;
            WorldServer server = fixture.Server!;

            await using var human = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "tunnel-bot");
            var (session, field, bot) = await SetupBotMatchAsync(
                server, human, fixture, BotDifficulty.Normal);

            PlayerRec humanRecord = field.Slots[session.FieldSeat];
            humanRecord.UsesTunneling = true;
            var spot = new BotVector(1000, 0, 1000);
            humanRecord.Position = spot;
            field.Slots[bot.Seat].Position = spot;
            bot.Position = spot;

            byte[] movement = human.WaitFor(
                frame => IsTunneledBotAction(frame, BotMovement.MoveType, 21),
                JourneyHelper.Timeout);
            Assert.Equal((byte)bot.Seat, (byte)(movement[8] & 0x1f));

            human.SendConfirmedBotHit(fixture.UdpPort2, bot.Seat);
            byte[] damage = human.WaitFor(
                frame => IsTunneledBotAction(frame, BotMovement.AttackType, 7) &&
                    frame[7] == (byte)PlayerAnimationKind.Damage,
                JourneyHelper.Timeout);
            Assert.Equal((byte)bot.Seat, damage[6]);
        }

        private static bool IsBotDamage(byte[] packet, byte botSeat) =>
            packet.Length == GameplayActionDatagram.ExtendedAnimationSize &&
            packet[0] == 0x11 && packet[1] == 0x03 && packet[6] == botSeat &&
            packet[8] == (byte)PlayerAnimationKind.Damage;

        private static bool Frame4f(byte[] frame)
        {
            // frame de campo do servidor: conteúdo decifrado começa por [u16 msgType=0x4f]...
            return frame.Length >= 7 && frame[0] == 0x4f && frame[1] == 0x00;
        }

        private static bool IsTunneledBotAction(byte[] frame, ushort type, ushort payloadLength)
        {
            return frame.Length >= payloadLength + 4 && frame[0] == 0x57 && frame[1] == 0 &&
                BitConverter.ToUInt16(frame, 2) == payloadLength &&
                BitConverter.ToUInt16(frame, 4) == type;
        }

        private static async Task<(ClientSession hs, Field field, BotPlayer bot)> SetupBotMatchAsync(
            WorldServer server, HeadlessWorldClient human, WorldServerFixture fixture, BotDifficulty diff)
        {
            human.Login("test", "test");
            human.WaitForFirstByte(0x0C, JourneyHelper.Timeout);
            human.SelectCharacter(1);
            ClientSession hs = JourneyHelper.WaitForSession(server, "test",
                s => s.ActiveCharId > 0 && s.Status == UserStatus.FieldLobby);

            human.CreateRoom(HeadlessWorldClient.RoomSpec.Golem("bot-stage"));
            JourneyHelper.WaitUntil(() => hs.FieldId >= 0 && server.GetField(hs.FieldId) != null, "sala não criada");
            Field field = server.GetField(hs.FieldId)!;
            var add = server.Bots.AddBotToField(field, hs, diff);
            Assert.True(add.Ok, add.Message);

            human.OpenUdp();
            human.UdpHandshake(fixture.UdpPort2, hs.Slot, hs.UdpKey);
            JourneyHelper.WaitUntil(() => hs.UdpEndpoint != null, "endpoint UDP não autenticado");
            human.WaitForUdp(p => p.Length == 12 && p[0] == 0x01 && p[1] == 0x02, JourneyHelper.Timeout);
            field.Slots[hs.FieldSeat].UsesTunneling = false;

            lock (field.SyncRoot)
            {
                field.State = 2;
                field.Phase = MatchPhase.Playing;
                field.Slots[hs.FieldSeat].State = 4;
            }
            await Task.CompletedTask;
            return (hs, field, field.Slots[add.Seat].Bot!);
        }
    }
}
