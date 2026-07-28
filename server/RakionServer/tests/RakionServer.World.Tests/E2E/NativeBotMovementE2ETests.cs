using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E;

[Collection("E2E")]
public sealed class NativeBotMovementE2ETests
{
    [Fact]
    [Trait("Requires", "BotEngineFixture")]
    public async Task NativeSnapshotPublishesThroughGameplayChannel()
    {
        NativeMatch? setup = await StartNativeMatchAsync("native-movement");
        if (setup == null)
            return;
        await using NativeMatch match = setup;
        PlayerRec botRecord = match.BotRecord;

        JourneyHelper.WaitUntil(
            () => botRecord.Bot!.MoveSeq > 0,
            "snapshot nativo inicial não chegou");
        BotVector origin = botRecord.Bot!.Position;
        BotVector target = origin with
        {
            X = origin.X + 10f,
            Z = origin.Z + 5f,
        };
        lock (match.Field.SyncRoot)
            match.HumanRecord.Position = target;
        match.Human.SendMove(
            match.Fixture.UdpPort2,
            match.Session.FieldSeat,
            ToWire(target.X),
            ToWire(target.Y),
            ToWire(target.Z));
        JourneyHelper.WaitUntil(
            () => botRecord.Bot!.EngineControls.HasFlag(BotControls.W),
            "World não aplicou input W à fonte nativa");

        byte[] movement = match.Human.WaitForUdp(
            packet => packet.Length == 26 &&
                packet[0] == 0x0a &&
                packet[1] == 0x03 &&
                packet[6] == botRecord.Slot,
            JourneyHelper.Timeout);

        Assert.Equal((byte)botRecord.Slot, movement[6]);
        Assert.True(BotMovement.TryReadPosition(movement, out BotVector published));
        Assert.True(float.IsFinite(published.X));
        Assert.True(float.IsFinite(published.Y));
        Assert.True(float.IsFinite(published.Z));
    }

    [Fact]
    [Trait("Requires", "BotEngineFixture")]
    public async Task HumanAttackDamagesNativeBotThroughWorldAuthority()
    {
        NativeMatch? setup = await StartNativeMatchAsync("native-combat");
        if (setup == null)
            return;
        await using NativeMatch match = setup;
        PlayerRec botRecord = match.BotRecord;
        JourneyHelper.WaitUntil(
            () => botRecord.Bot!.MoveSeq > 0,
            "snapshot nativo inicial não chegou");
        PositionAttackerInFront(match);
        int initialHealth = botRecord.Bot!.Health;

        match.Human.SendBotTelemetryAttack(
            match.Fixture.UdpPort2,
            match.Session.FieldSeat);
        JourneyHelper.WaitUntil(
            () => botRecord.Bot.Health < initialHealth,
            "ataque autenticado não reduziu o HP autoritativo do bot");
        byte[] damage = match.Human.WaitForUdp(
            packet => packet.Length == 12 &&
                packet[0] == 0x11 &&
                packet[1] == 0x03 &&
                packet[6] == botRecord.Slot &&
                packet[8] == (byte)PlayerAnimationKind.Damage,
            JourneyHelper.Timeout);

        Assert.Equal(match.Session.FieldSeat, damage[11]);
        Assert.Equal(match.Session.FieldSeat, botRecord.Bot.LastAttackerSeat);
        Assert.Equal(1u, botRecord.Bot.DamageSequence);
    }

    [Fact]
    [Trait("Requires", "BotEngineFixture")]
    public async Task NativeBotDiesScoresAndRespawnsFromAuthoritativeHits()
    {
        NativeMatch? setup = await StartNativeMatchAsync("native-lifecycle");
        if (setup == null)
            return;
        await using NativeMatch match = setup;
        PlayerRec botRecord = match.BotRecord;
        JourneyHelper.WaitUntil(
            () => botRecord.Bot!.MoveSeq > 0,
            "snapshot nativo inicial não chegou");
        PositionAttackerInFront(match);
        uint initialLifecycle = botRecord.Bot!.LifecycleSequence;

        for (uint sequence = 2; botRecord.Bot.Alive && sequence < 20; ++sequence)
        {
            uint expectedDamageSequence = botRecord.Bot.DamageSequence + 1;
            match.Human.SendBotTelemetryAttack(
                match.Fixture.UdpPort2,
                match.Session.FieldSeat,
                sequence);
            JourneyHelper.WaitUntil(
                () => botRecord.Bot.DamageSequence >= expectedDamageSequence,
                $"golpe {sequence} não foi confirmado");
            await Task.Delay(140);
        }

        Assert.False(botRecord.Bot.Alive);
        Assert.Equal(0, botRecord.Bot.Health);
        Assert.Equal(initialLifecycle + 1, botRecord.Bot.LifecycleSequence);
        match.Human.WaitForFirstByte(0x4F, JourneyHelper.Timeout);
        Assert.Equal(1, match.HumanRecord.RoundScore);

        JourneyHelper.WaitUntil(
            () => botRecord.Bot.Alive,
            "bot não renasceu após o timer autoritativo",
            TimeSpan.FromSeconds(10));
        Assert.Equal(botRecord.Bot.MaxHealth, botRecord.Bot.Health);
        Assert.Equal(initialLifecycle + 2, botRecord.Bot.LifecycleSequence);
        match.Human.WaitForUdp(
            packet => packet.Length == GameplayActionDatagram.MoveSize &&
                packet[0] == 0x0A &&
                packet[1] == 0x03 &&
                packet[6] == botRecord.Slot,
            JourneyHelper.Timeout);
    }

    [Fact]
    [Trait("Requires", "BotEngineFixture")]
    public async Task NativeBotDamagesKillsAndRespawnsHumanAuthoritatively()
    {
        NativeMatch? setup = await StartNativeMatchAsync("native-bot-combat");
        if (setup == null)
            return;
        await using NativeMatch match = setup;
        JourneyHelper.WaitUntil(
            () => match.BotRecord.Bot!.MoveSeq > 0,
            "snapshot nativo inicial não chegou");
        lock (match.Field.SyncRoot)
            match.HumanRecord.Vitals.Initialize(30, 0);
        PositionAttackerInFront(match);
        JourneyHelper.WaitUntil(
            () => match.BotRecord.Bot!.TargetSeat ==
                match.HumanRecord.Slot,
            "cérebro nativo não selecionou o humano");
        JourneyHelper.WaitUntil(
            () => match.BotRecord.Bot!.NextAttackReadyMs > 0,
            "cérebro nativo não abriu uma janela de ataque");

        JourneyHelper.WaitUntil(
            () => match.HumanRecord.Dead,
            "ataque nativo do bot não matou o humano",
            TimeSpan.FromSeconds(10));
        match.Human.WaitForFirstByte(0x4F, JourneyHelper.Timeout);
        Assert.Equal(0, match.HumanRecord.Vitals.Hp);
        Assert.Equal(1, match.BotRecord.RoundScore);

        byte[] respawn = match.Human.WaitForUdp(
            packet => GameplayPeerDatagramCodec.TryParseEntityEvent(
                    packet, out GameplayEntityEvent envelope) &&
                envelope.EventId == GameplayPeerDatagramCodec.RespawnEventId &&
                envelope.PrimaryEntitySeat == match.HumanRecord.Slot,
            TimeSpan.FromSeconds(10));
        Assert.NotEmpty(respawn);
        JourneyHelper.WaitUntil(
            () => !match.HumanRecord.Dead,
            "humano não renasceu após lifecycle autoritativo");
        Assert.Equal(30, match.HumanRecord.Vitals.Hp);
    }

    [Fact]
    [Trait("Requires", "BotEngineFixture")]
    public async Task MultipleNativeBotsKeepIndependentEngineState()
    {
        NativeMatch? setup = await StartNativeMatchAsync(
            "native-multi-bot", botCount: 2);
        if (setup == null)
            return;
        await using NativeMatch match = setup;
        PlayerRec[] bots = match.Field.BotSlots.ToArray();
        Assert.Equal(2, bots.Length);
        BotPlayer first = bots[0].Bot!;
        BotPlayer second = bots[1].Bot!;
        JourneyHelper.WaitUntil(
            () => first.MoveSeq > 0 && second.MoveSeq > 0,
            "snapshots nativos multi-bot não chegaram");
        uint firstSeq = first.MoveSeq;
        uint secondSeq = second.MoveSeq;
        first.SetEngineIntent(BotControls.W, false);
        second.SetEngineIntent(BotControls.S, true);
        first.BeginHitReaction(Environment.TickCount64);

        Assert.Equal(BotControls.None, first.EngineControls);
        Assert.Equal(BotControls.S, second.EngineControls);
        Assert.True(second.EngineAttacking);
        Assert.True(first.HitReactionUntilMs > 0);
        Assert.Equal(0, second.HitReactionUntilMs);
        Assert.NotSame(first.Combat, second.Combat);
        Assert.True(first.EngineAttached);
        Assert.True(second.EngineAttached);
        Assert.True(firstSeq > 0 && secondSeq > 0);
    }

    private static short ToWire(float value) =>
        checked((short)MathF.Round(value * BotMovement.PositionScale));

    private static void PositionAttackerInFront(NativeMatch match)
    {
        BotVector botPosition = match.BotRecord.Position;
        BotVector attackerPosition = botPosition with
        {
            Z = botPosition.Z + 2f,
        };
        match.Human.SendBotTelemetryMove(
            match.Fixture.UdpPort2,
            match.Session.FieldSeat,
            ToWire(attackerPosition.X),
            ToWire(attackerPosition.Y),
            ToWire(attackerPosition.Z));
        JourneyHelper.WaitUntil(
            () => match.HumanRecord.Position.HorizontalDistanceTo(
                attackerPosition) < 0.05f,
            "World não recebeu a posição autenticada do atacante");
    }

    private static async Task<NativeMatch?> StartNativeMatchAsync(
        string roomName,
        int botCount = 1)
    {
        string? hostPath = Environment.GetEnvironmentVariable(
            "RAKION_BOT_ENGINE_HOST");
        string? clientRoot = Environment.GetEnvironmentVariable(
            "RAKION_BOT_ENGINE_CLIENT_ROOT");
        if (string.IsNullOrWhiteSpace(hostPath) ||
            string.IsNullOrWhiteSpace(clientRoot))
            return null;
        Assert.True(File.Exists(hostPath), hostPath);
        Assert.True(Directory.Exists(clientRoot), clientRoot);
        WorldServerFixture fixture = await WorldServerFixture.CreateAsync(
            configure: config =>
            {
                config.BotEngine.Enabled = true;
                config.BotEngine.HostPath = hostPath;
                config.BotEngine.ClientRoot = clientRoot;
                config.BotEngine.MaxBotsPerField = Math.Max(4, botCount);
            });
        if (!fixture.Available)
        {
            await fixture.DisposeAsync();
            return null;
        }
        return await CreateNativeMatchAsync(fixture, roomName, botCount);
    }

    private static async Task<NativeMatch> CreateNativeMatchAsync(
        WorldServerFixture fixture,
        string roomName,
        int botCount)
    {
        WorldServer server = fixture.Server!;
        HeadlessWorldClient human = await HeadlessWorldClient.ConnectAsync(
            WorldServerFixture.Host, fixture.TcpPort, roomName);
        human.Login("test", "test");
        human.WaitForFirstByte(0x0C, JourneyHelper.Timeout);
        human.SelectCharacter(1);
        ClientSession session = JourneyHelper.WaitForSession(
            server,
            "test",
            value => value.ActiveCharId > 0 &&
                value.Status == UserStatus.FieldLobby);
        human.CreateRoom(new HeadlessWorldClient.RoomSpec(
            roomName,
            HeadlessWorldClient.RoomSpec.BattleMap,
            (byte)GameMode.Deathmatch, 1, 432, 20, 1, 99));
        JourneyHelper.WaitUntil(
            () => session.FieldId >= 0 &&
                server.GetField(session.FieldId) != null,
            "sala não criada");
        Field field = server.GetField(session.FieldId)!;
        for (int index = 0; index < botCount; index++)
            human.SendFieldChat("GoHeroi : /addbot");
        JourneyHelper.WaitUntil(
            () => field.BotSlots.Count(record =>
                record.Bot!.EngineAttached) >= botCount,
            "Host nativo não confirmou os bots",
            TimeSpan.FromSeconds(60));
        PlayerRec botRecord = field.BotSlots.First();
        AuthenticateGameplay(human, fixture, session, field);
        lock (field.SyncRoot)
        {
            field.State = 2;
            field.Phase = MatchPhase.Playing;
            field.DeadlineMs = Environment.TickCount64 + 60_000;
            field.Round = 1;
            field.Slots[session.FieldSeat].State = 4;
            foreach (PlayerRec bot in field.BotSlots)
                bot.State = 4;
        }
        return new NativeMatch(
            fixture,
            human,
            session,
            field,
            field.Slots[session.FieldSeat],
            botRecord);
    }

    private static void AuthenticateGameplay(
        HeadlessWorldClient human,
        WorldServerFixture fixture,
        ClientSession session,
        Field field)
    {
        human.OpenUdp();
        human.UdpHandshake(fixture.UdpPort2, session.Slot, session.UdpKey);
        JourneyHelper.WaitUntil(
            () => session.UdpEndpoint != null,
            "endpoint UDP não autenticado");
        human.WaitForUdp(
            packet => packet.Length == 12 &&
                packet[0] == 0x01 &&
                packet[1] == 0x02,
            JourneyHelper.Timeout);
        field.Slots[session.FieldSeat].UsesTunneling = false;
    }

    private sealed record NativeMatch(
        WorldServerFixture Fixture,
        HeadlessWorldClient Human,
        ClientSession Session,
        Field Field,
        PlayerRec HumanRecord,
        PlayerRec BotRecord) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Human.DisposeAsync();
            await Fixture.DisposeAsync();
        }
    }
}
