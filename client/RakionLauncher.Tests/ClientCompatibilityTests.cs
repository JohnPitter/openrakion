using RakionLauncher;
using Xunit;

namespace RakionLauncher.Tests;

public sealed class ClientCompatibilityTests
{
    [Fact]
    public void GameEnvironment_RunsLegacyClientWithoutElevation()
    {
        string block = GameLauncher.BuildEnvironmentBlock();

        Assert.Contains("__COMPAT_LAYER=RunAsInvoker\0", block, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("\0", block);
    }

    [Fact]
    public void Install_DeploysProxyAndClientPatchAndRemovesLegacyForwarder()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "rakion-compat-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "verorig.dll"), "legacy");

            ClientCompatibility.Install(root);

            Assert.True(File.Exists(Path.Combine(root, "version.dll")));
            Assert.True(File.Exists(Path.Combine(root, "RakionClientPatch.dll")));
            Assert.False(File.Exists(Path.Combine(root, "verorig.dll")));
            Assert.DoesNotContain(
                "RakionLauncher.Native.verorig.dll",
                typeof(ClientCompatibility).Assembly.GetManifestResourceNames());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ValidateInstalled_RejectsReleaseThatRemovedClientPatch()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "rakion-compat-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            ClientCompatibility.Install(root);
            File.Delete(Path.Combine(root, "RakionClientPatch.dll"));

            Assert.Throws<FileNotFoundException>(() => ClientCompatibility.ValidateInstalled(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
