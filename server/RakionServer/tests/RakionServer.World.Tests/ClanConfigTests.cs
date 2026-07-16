using System;
using System.IO;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class ClanConfigTests
{
    [Fact]
    public void ClanIsDisabledByDefault()
    {
        var config = new WorldConfig();

        Assert.False(config.Clan.Enabled);
        Assert.Equal(99, config.Clan.MaxMembers);
        Assert.Equal(7, config.Clan.TreeMaxChildren);
    }

    [Fact]
    public void LoadsAndClampsLegacyClanLimits()
    {
        string path = Path.Combine(Path.GetTempPath(), $"world-{Guid.NewGuid():N}.ini");
        File.WriteAllText(path,
            "[Clan]\nEnabled=1\nMaxMembers=500\nTreeMaxChildren=12\n");
        try
        {
            WorldConfig config = WorldConfig.Load(path);

            Assert.True(config.Clan.Enabled);
            Assert.Equal(99, config.Clan.MaxMembers);
            Assert.Equal(7, config.Clan.TreeMaxChildren);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
