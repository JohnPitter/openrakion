using System;
using System.IO;
using System.Threading.Tasks;
using RakionServer.Common;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class ClientIntegrityConfigTests
{
    [Fact]
    public void LoadsRequiredLauncherBuildAsOneIdentity()
    {
        string path = WriteConfig("RequiredAppId=11001\nRequiredBuildVersion=259");
        try
        {
            WorldConfig config = WorldConfig.Load(path);
            Assert.Equal(new LauncherBuildIdentity(11001, 259), config.RequiredClientBuild);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsHalfConfiguredLauncherBuild()
    {
        string path = WriteConfig("RequiredAppId=11001\nRequiredBuildVersion=0");
        try
        {
            Assert.Throws<InvalidDataException>(() => WorldConfig.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RuntimeHashReplacementNeverExposesMixedPair()
    {
        var config = new WorldConfig();
        config.UpdateClientHashes("A1", "A2");

        Parallel.For(0, 10_000, index =>
        {
            if ((index & 1) == 0)
                config.UpdateClientHashes("A1", "A2");
            else
                config.UpdateClientHashes("B1", "B2");
            var snapshot = config.ClientHashes;
            Assert.True(
                snapshot is { Md5_1: "A1", Md5_2: "A2" } or
                { Md5_1: "B1", Md5_2: "B2" });
        });
    }

    private static string WriteConfig(string clientValues)
    {
        string path = Path.Combine(Path.GetTempPath(), $"world-{Guid.NewGuid():N}.ini");
        File.WriteAllText(path, $"[Client]\nEnforceMD5=0\n{clientValues}\n");
        return path;
    }
}
