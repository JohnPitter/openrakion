using System;
using System.IO;
using System.Net.Sockets;
using RakionServer.World.BotEngine;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class BotEngineIsolationTests
{
    [Fact]
    public void MultipleBotsKeepIndependentCombatAndControls()
    {
        Field field = PlayingField();
        PlayerRec human = field.Slots[0];
        human.Position = new BotVector(0, 0, 2);
        PlayerRec left = AddBot(field, 1, new BotVector(-1, 0, 0));
        PlayerRec right = AddBot(field, 1, new BotVector(1, 0, 0));

        Assert.True(left.Bot!.TryStartAttack(1_000));
        Assert.True(right.Bot!.TryStartAttack(1_000));
        left.Bot.SetEngineIntent(BotControls.W, false);
        right.Bot.SetEngineIntent(BotControls.S, true);
        left.Bot.BeginHitReaction(1_100);

        Assert.Equal(BotControls.None, left.Bot.EngineControls);
        Assert.False(left.Bot.EngineAttacking);
        Assert.Equal(BotControls.S, right.Bot.EngineControls);
        Assert.True(right.Bot.EngineAttacking);
        Assert.NotSame(left.Bot.Combat, right.Bot.Combat);
        Assert.False(BotEngineBrain.TryPlan(
            field, (byte)left.Slot, 1, 1_200, out _));
        Assert.True(BotEngineBrain.TryPlan(
            field, (byte)right.Slot, 2, 1_200, out BotEngineIntent intent));
        Assert.Equal((byte)human.Slot, intent.TargetSeat);
    }

    [Fact]
    public void FallenBotDoesNotPlanMovementOrAttack()
    {
        Field field = PlayingField();
        field.Slots[0].Position = new BotVector(0, 0, 2);
        PlayerRec bot = AddBot(field, 1, BotVector.Zero);
        bot.Bot!.BeginHitReaction(5_000);

        Assert.False(BotEngineBrain.TryPlan(
            field, (byte)bot.Slot, 1, 5_100, out _));
        Assert.Equal(BotControls.None, bot.Bot.EngineControls);
    }

    [Fact]
    public void ShippedSourcesDoNotContainSyntheticTickPath()
    {
        string worldRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "RakionServer.World"));
        Assert.True(Directory.Exists(worldRoot), worldRoot);
        Assert.False(File.Exists(Path.Combine(worldRoot, "BotManager.Tick.cs")));
        Assert.False(File.Exists(Path.Combine(worldRoot, "Domain", "BotSteering.cs")));
        Assert.False(File.Exists(Path.Combine(
            worldRoot, "Domain", "BotNavigationPlanner.cs")));
        string botEngine = File.ReadAllText(
            Path.Combine(worldRoot, "WorldServer.BotEngine.cs"));
        Assert.Contains("SyncNativeBotsAsync", botEngine);
        Assert.DoesNotContain("Bots.TickField", botEngine);
        Assert.DoesNotContain("TickNavigated", botEngine);
    }

    private static Field PlayingField()
    {
        Field field = new(1)
        {
            Mode = (byte)GameMode.Deathmatch,
            State = 2,
            Phase = MatchPhase.Playing
        };
        var server = new WorldServer(
            new WorldConfig(), new WorldDatabase(new WorldConfig().Db));
        ClientSession session = new(
            new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp),
            1,
            server);
        field.Slots[0].Session = session;
        field.Slots[0].State = 4;
        field.Slots[0].Position = BotVector.Zero;
        return field;
    }

    private static PlayerRec AddBot(Field field, byte team, BotVector position)
    {
        BotPlayer bot = new()
        {
            Name = "B",
            Team = team,
            Position = position,
            Profile = BotProfile.Normal
        };
        bot.InitHealth(1);
        bot.AttachEngine();
        int seat = field.AddBot(bot, team);
        PlayerRec record = field.Slots[seat];
        record.State = 4;
        record.Position = position;
        return record;
    }
}
