using System;
using System.IO;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class BotEngineConfigTests
{
    [Fact]
    public void BotEngineIsDisabledByDefault()
    {
        var config = new WorldConfig();

        Assert.False(config.BotEngine.Enabled);
        Assert.Equal(4, config.BotEngine.MaxBotsPerField);
    }

    [Fact]
    public void LoadsPathsRelativeToWorldConfiguration()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), $"bot-engine-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "worldserver.ini");
        File.WriteAllText(path,
            "[BotEngine]\nEnabled=1\nHostPath=Bin\\BotEngineHost.exe\n" +
            "ClientRoot=Client\nMaxBotsPerField=20\n");
        try
        {
            WorldConfig config = WorldConfig.Load(path);

            Assert.True(config.BotEngine.Enabled);
            Assert.Equal(
                Path.Combine(directory, "Bin", "BotEngineHost.exe"),
                config.BotEngine.HostPath);
            Assert.Equal(Path.Combine(directory, "Client"), config.BotEngine.ClientRoot);
            Assert.Equal(4, config.BotEngine.MaxBotsPerField);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
