using RakionLauncher;
using Xunit;

namespace RakionLauncher.Tests;

public sealed class GameLauncherTests
{
    [Fact]
    public void BuildEnvironmentBlock_DoesNotExposePuppetPassword()
    {
        string? original = Environment.GetEnvironmentVariable(PuppetLaunch.PasswordVariable);
        try
        {
            Environment.SetEnvironmentVariable(PuppetLaunch.PasswordVariable, "secret");

            string block = GameLauncher.BuildEnvironmentBlock();

            Assert.DoesNotContain(
                $"{PuppetLaunch.PasswordVariable}=", block, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(PuppetLaunch.PasswordVariable, original);
        }
    }

    [Fact]
    public void IsGameExecutable_AcceptsSameExecutableIgnoringCase()
    {
        string expected = Path.Combine(Path.GetTempPath(), "OpenRakion", "Bin", "rakion.exe");
        string actual = expected.ToUpperInvariant();

        Assert.True(GameLauncher.IsGameExecutable(actual, expected));
    }

    [Fact]
    public void IsGameExecutable_RejectsRakionFromAnotherClient()
    {
        string expected = Path.Combine(Path.GetTempPath(), "OpenRakion", "Bin", "rakion.exe");
        string actual = Path.Combine(Path.GetTempPath(), "RakionOriginal", "Bin", "rakion.exe");

        Assert.False(GameLauncher.IsGameExecutable(actual, expected));
    }

    [Fact]
    public void IsGameExecutable_RejectsMissingPath()
    {
        string expected = Path.Combine(Path.GetTempPath(), "OpenRakion", "Bin", "rakion.exe");

        Assert.False(GameLauncher.IsGameExecutable(null, expected));
    }
}
