using Xunit;
using RakionClientRuntime;

namespace RakionBotHost.Tests;

public sealed class BotHostOptionsTests
{
    [Fact]
    public void Parse_AcceptsClientRootWithSpaces()
    {
        using var credential = new EnvironmentVariableScope(
            BotHostOptions.CredentialVariable, "secret");
        string root = Path.Combine(Path.GetTempPath(), "Rakion Original");

        BotHostOptions options = BotHostOptions.Parse(
            ["--client-root", root, "--user", "bot_host", "--field", "7",
                "--world", "LevelsSV/Cage/Cage.wld"]);

        Assert.Equal(Path.GetFullPath(root), options.ClientRoot);
        Assert.Equal("bot_host", options.User);
        Assert.Equal("secret", options.Credential);
        Assert.Equal("1A", options.ServerId);
        Assert.Equal(7, options.FieldId);
        Assert.Equal(HeadlessPeerRole.Joiner, options.Role);
        Assert.Equal(@"LevelsSV\Cage\Cage.wld", options.WorldName);
    }

    [Fact]
    public void Parse_RequiresCredentialOutsideCommandLine()
    {
        using var credential = new EnvironmentVariableScope(
            BotHostOptions.CredentialVariable, null);

        Assert.Throws<InvalidOperationException>(() => BotHostOptions.Parse(
            ["--client-root", "client", "--user", "bot_host", "--field", "7",
                "--world", @"LevelsSV\Cage\Cage.wld"]));
    }

    [Fact]
    public void Parse_RejectsInvalidField()
    {
        using var credential = new EnvironmentVariableScope(
            BotHostOptions.CredentialVariable, "secret");

        Assert.Throws<ArgumentException>(() => BotHostOptions.Parse(
            ["--client-root", "client", "--user", "bot_host", "--field", "0",
                "--world", @"LevelsSV\Cage\Cage.wld"]));
    }

    [Fact]
    public void Parse_AcceptsMasterRoomWithoutField()
    {
        using var credential = new EnvironmentVariableScope(
            BotHostOptions.CredentialVariable, "secret");

        BotHostOptions options = BotHostOptions.Parse(
            ["--client-root", "client", "--user", "master", "--role", "master",
                "--room", "native-headless", "--world", @"LevelsSV\Cage\Cage.wld"]);

        Assert.Equal(HeadlessPeerRole.Master, options.Role);
        Assert.Null(options.FieldId);
        Assert.Equal("native-headless", options.RoomName);
        Assert.Equal(209, BattleMapCatalog.Resolve(options.WorldName));
    }

    [Fact]
    public void BattleMapCatalog_ResolvesMammothFromNormalizedWorld()
    {
        Assert.Equal(
            211,
            BattleMapCatalog.Resolve(@"LevelsSV/Mammoth/Mammoth.wld"));
    }

    [Fact]
    public void Parse_AcceptsQuickJoin()
    {
        using var credential = new EnvironmentVariableScope(
            BotHostOptions.CredentialVariable, "secret");

        BotHostOptions options = BotHostOptions.Parse(
            ["--client-root", "client", "--user", "joiner", "--field", "quick",
                "--world", @"LevelsSV\Cage\Cage.wld"]);

        Assert.Equal(HeadlessPeerRole.Joiner, options.Role);
        Assert.Null(options.FieldId);
    }

    [Fact]
    public void Parse_RejectsWorldOutsideLevelArchive()
    {
        using var credential = new EnvironmentVariableScope(
            BotHostOptions.CredentialVariable, "secret");

        Assert.Throws<ArgumentException>(() => BotHostOptions.Parse(
            ["--client-root", "client", "--user", "bot_host", "--field", "7",
                "--world", @"..\Cage.wld"]));
    }

    [Fact]
    public void EnvironmentBlock_EnablesHeadlessWithoutCredential()
    {
        using var credential = new EnvironmentVariableScope(
            BotHostOptions.CredentialVariable, "secret");

        string block = LegacyClientProcess.BuildEnvironmentBlock(
            new Dictionary<string, string>
            {
                ["OPENRAKION_HEADLESS"] = "1",
                ["OPENRAKION_HEADLESS_FIELD"] = "7"
            },
            new HashSet<string> { BotHostOptions.CredentialVariable });

        Assert.Contains("OPENRAKION_HEADLESS=1", block);
        Assert.Contains("OPENRAKION_HEADLESS_FIELD=7", block);
        Assert.DoesNotContain(
            $"{BotHostOptions.CredentialVariable}=", block,
            StringComparison.OrdinalIgnoreCase);
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
