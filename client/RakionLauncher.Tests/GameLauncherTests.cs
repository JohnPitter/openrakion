using RakionLauncher;
using Xunit;

namespace RakionLauncher.Tests;

public sealed class GameLauncherTests
{
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
