namespace RakionLauncher.Tests;

using Xunit;

public sealed class PuppetLaunchTests
{
    [Fact]
    public void Parse_ReadsPasswordFromEnvironment()
    {
        using var variable = new EnvironmentVariableScope("RAKION_PUPPET_PASSWORD", "secret");

        PuppetLaunchOptions options = PuppetLaunch.Parse(["--puppet", "bot_1", "1A"]);

        Assert.Equal("bot_1", options.User);
        Assert.Equal("secret", options.Password);
        Assert.Equal("1A", options.ServerId);
    }

    [Fact]
    public void Parse_RejectsPasswordOnCommandLine()
    {
        using var variable = new EnvironmentVariableScope("RAKION_PUPPET_PASSWORD", "secret");

        Assert.Throws<ArgumentException>(() =>
            PuppetLaunch.Parse(["--puppet", "bot_1", "secret", "1A"]));
    }

    [Fact]
    public void Parse_RequiresPasswordEnvironmentVariable()
    {
        using var variable = new EnvironmentVariableScope("RAKION_PUPPET_PASSWORD", null);

        Assert.Throws<InvalidOperationException>(() =>
            PuppetLaunch.Parse(["--puppet", "bot_1"]));
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _original;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _original = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _original);
    }
}
