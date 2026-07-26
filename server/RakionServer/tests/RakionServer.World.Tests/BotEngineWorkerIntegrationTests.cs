using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        BotEngineBot bot = await first.AddBotAsync(
            new BotEngineBotRequest(1, "BotProbe", "Archer"),
            CancellationToken.None);
        BotEngineHealth populatedHealth = await supervisor.PingFieldAsync(
            field.FieldId, CancellationToken.None);

        Assert.Same(first, second);
        Assert.True(first.IsRunning);
        Assert.Equal(1, supervisor.Count);
        Assert.Equal(field.FieldId, health.FieldId);
        Assert.Equal(0u, health.BotCount);
        Assert.Equal(1u, bot.BotId);
        Assert.Equal(1u, bot.ActivePlayers);
        Assert.Equal(4u, bot.Capacity);
        Assert.Equal(1u, populatedHealth.BotCount);

        await supervisor.StopFieldAsync(field.FieldId, CancellationToken.None);
        Assert.False(first.IsRunning);
        Assert.Equal(0, supervisor.Count);
    }
}
