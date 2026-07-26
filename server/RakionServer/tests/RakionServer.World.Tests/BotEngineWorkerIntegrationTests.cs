using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using RakionServer.World.BotEngine;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class BotEngineWorkerIntegrationTests
{
    [Fact]
    [Trait("Requires", "BotEngineFixture")]
    public async Task SupervisorOwnsPersistentNativeHostWhenFixtureIsConfigured()
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
        var options = new BotEngineWorkerOptions(
            hostPath,
            clientRoot,
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(5));
        await using var supervisor = new BotEngineSupervisor(options);
        var field = new BotEngineFieldRequest(
            4242, 4, 211, 2, @"LevelsSV\Mammoth\Mammoth.wld");

        BotEngineWorker first = await supervisor.StartFieldAsync(
            field, CancellationToken.None);
        BotEngineWorker second = await supervisor.StartFieldAsync(
            field, CancellationToken.None);
        BotEngineHealth health = await supervisor.PingFieldAsync(
            field.FieldId, CancellationToken.None);
        var bots = new BotEngineBot[4];
        for (int index = 0; index < bots.Length; ++index)
        {
            bots[index] = await first.AddBotAsync(
                new BotEngineBotRequest(
                    (uint)(index + 1), $"BotProbe{index + 1}", "Archer"),
                CancellationToken.None);
        }
        BotEngineTick tick = await first.TickAsync(1, CancellationToken.None);
        var snapshots = new BotEnginePlayerSnapshot[bots.Length];
        for (int index = 0; index < bots.Length; ++index)
        {
            snapshots[index] = await first.SnapshotAsync(
                bots[index].BotId, CancellationToken.None);
        }
        BotEngineHealth populatedHealth = await supervisor.PingFieldAsync(
            field.FieldId, CancellationToken.None);

        Assert.Same(first, second);
        Assert.True(first.IsRunning);
        Assert.Equal(1, supervisor.Count);
        Assert.Equal(field.FieldId, health.FieldId);
        Assert.Equal(0u, health.BotCount);
        Assert.Equal(4u, bots[^1].ActivePlayers);
        Assert.All(bots, bot => Assert.Equal(4u, bot.Capacity));
        Assert.Equal(1u, tick.FrameCount);
        Assert.Equal(4u, tick.ActivePlayers);
        Assert.All(snapshots, snapshot =>
        {
            Assert.True(snapshot.Ready);
            Assert.True(float.IsFinite(snapshot.X));
            Assert.True(float.IsFinite(snapshot.Y));
            Assert.True(float.IsFinite(snapshot.Z));
            Assert.True(float.IsFinite(snapshot.Hp));
        });
        Assert.Equal(4u, populatedHealth.BotCount);

        BotEnginePlayerSnapshot origin = snapshots[0];
        BotEnginePlayerSnapshot moved = origin;
        for (int attempt = 0; attempt < 50 && !HasMoved(origin, moved); ++attempt)
        {
            await first.ApplyInputAsync(
                bots[0].BotId,
                BotEngineInput.Forward,
                CancellationToken.None);
            await first.TickAsync(1, CancellationToken.None);
            moved = await first.SnapshotAsync(
                bots[0].BotId, CancellationToken.None);
            await Task.Delay(20);
        }
        await first.ApplyInputAsync(
            bots[0].BotId,
            BotEngineInput.None,
            CancellationToken.None);
        Assert.True(HasMoved(origin, moved));

        await supervisor.StopFieldAsync(field.FieldId, CancellationToken.None);
        Assert.False(first.IsRunning);
        Assert.Equal(0, supervisor.Count);
    }

    [Fact]
    [Trait("Requires", "BotEngineFixture")]
    public async Task CoordinatorMapsWorldFieldAndSynchronizesNativeSnapshot()
    {
        string? hostPath = Environment.GetEnvironmentVariable(
            "RAKION_BOT_ENGINE_HOST");
        string? clientRoot = Environment.GetEnvironmentVariable(
            "RAKION_BOT_ENGINE_CLIENT_ROOT");
        if (string.IsNullOrWhiteSpace(hostPath) ||
            string.IsNullOrWhiteSpace(clientRoot))
            return;

        var config = new WorldConfig.BotEngineConfig
        {
            Enabled = true,
            HostPath = hostPath,
            ClientRoot = clientRoot,
        };
        await using var coordinator = new BotEngineCoordinator(config);
        var field = new Field(4343)
        {
            MapId = 11,
            Mode = (byte)GameMode.Deathmatch,
        };
        var bot = new BotPlayer { Name = "NativeProbe", Team = 1 };
        bot.InitHealth(1);
        int seat = field.AddBot(bot, 1);

        await coordinator.AddBotAsync(
            field, (byte)seat, bot, CancellationToken.None);
        Assert.True(await coordinator.TickFieldAsync(
            field, CancellationToken.None));

        Assert.True(float.IsFinite(bot.Position.X));
        Assert.True(float.IsFinite(bot.Position.Y));
        Assert.True(float.IsFinite(bot.Position.Z));
        Assert.Equal(bot.Position, field.Slots[seat].Position);
        await coordinator.StopFieldAsync(field.Id, CancellationToken.None);
    }

    private static bool HasMoved(
        BotEnginePlayerSnapshot origin,
        BotEnginePlayerSnapshot current)
    {
        float x = current.X - origin.X;
        float y = current.Y - origin.Y;
        float z = current.Z - origin.Z;
        return x * x + y * y + z * z > 0.0001f;
    }
}
