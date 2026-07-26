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
            WorldServerFixture.Host, fixture.TcpPort, "native-movement");
        human.Login("test", "test");
        human.WaitForFirstByte(0x0C, JourneyHelper.Timeout);
        human.SelectCharacter(1);
        ClientSession session = JourneyHelper.WaitForSession(server, "test",
            value => value.ActiveCharId > 0 && value.Status == UserStatus.FieldLobby);
        human.CreateRoom(new HeadlessWorldClient.RoomSpec(
            "native-movement", 11, 1, 1, 432, 0, 1, 99));
        JourneyHelper.WaitUntil(
            () => session.FieldId >= 0 && server.GetField(session.FieldId) != null,
            "sala não criada");
        Field field = server.GetField(session.FieldId)!;
        human.SendFieldChat("GoHeroi : /addbot");
        JourneyHelper.WaitUntil(
            () => field.BotSlots.Any(record => record.Bot!.EngineAttached),
            "Host nativo não confirmou o bot",
            TimeSpan.FromSeconds(40));
        PlayerRec botRecord = field.BotSlots.Single();

        AuthenticateGameplay(human, fixture, session, field);
        lock (field.SyncRoot)
        {
            field.State = 2;
            field.Phase = MatchPhase.Playing;
            field.DeadlineMs = Environment.TickCount64 + 60_000;
            field.Round = 1;
            field.Slots[session.FieldSeat].State = 4;
            botRecord.State = 4;
        }
        JourneyHelper.WaitUntil(
            () => botRecord.Bot!.MoveSeq > 0,
            "snapshot nativo inicial não chegou");
        BotVector origin = botRecord.Bot!.Position;
        BotVector target = origin with
        {
            X = origin.X + 1000f,
            Z = origin.Z + 500f,
        };
        lock (field.SyncRoot)
            field.Slots[session.FieldSeat].Position = target;
        human.SendMove(
            fixture.UdpPort2,
            session.FieldSeat,
            (short)MathF.Round(target.X),
            (short)MathF.Round(target.Y),
            (short)MathF.Round(target.Z));
        JourneyHelper.WaitUntil(
            () => botRecord.Bot!.EngineControls.HasFlag(BotControls.W),
            "World não aplicou input W à fonte nativa");

        byte[] movement = human.WaitForUdp(
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
}
