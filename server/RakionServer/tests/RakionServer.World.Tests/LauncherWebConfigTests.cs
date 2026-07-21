using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using RakionServer.LauncherWeb;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class LauncherWebConfigTests
{
    [Fact]
    public void EnablesTicketAuthenticationByDefault()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Legacy:Enabled"] = "false",
                ["ConnectionStrings:Rakion"] = "Server=localhost;Database=rakion"
            })
            .Build();

        LauncherWebConfig config = LauncherWebConfig.Load(configuration, ".");

        Assert.True(config.TicketAuthEnabled);
    }

    [Fact]
    public void UsesServerSelectionCompatibleTicketLifetimeByDefault()
    {
        LauncherWebConfig config = LoadConfig(new Dictionary<string, string?>());

        Assert.Equal(900, config.TicketLifetimeSeconds);
    }

    [Fact]
    public void LimitsConfiguredTicketLifetimeToThirtyMinutes()
    {
        LauncherWebConfig config = LoadConfig(new Dictionary<string, string?>
        {
            ["Auth:TicketLifetimeSeconds"] = "3600"
        });

        Assert.Equal(1800, config.TicketLifetimeSeconds);
    }

    private static LauncherWebConfig LoadConfig(Dictionary<string, string?> values)
    {
        values["Legacy:Enabled"] = "false";
        values["Auth:Enabled"] = "false";
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return LauncherWebConfig.Load(configuration, ".");
    }
}
