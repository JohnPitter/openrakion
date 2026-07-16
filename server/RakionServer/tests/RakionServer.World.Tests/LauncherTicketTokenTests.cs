using System;
using RakionServer.Common;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class LauncherTicketTokenTests
{
    [Fact]
    public void GeneratedTicketFitsOriginalCredentialField()
    {
        string ticket = LauncherTicketToken.Create();

        Assert.Equal(Protocol.LoginLimits.Field3Max, ticket.Length);
        Assert.True(LauncherTicketToken.IsValidFormat(ticket));
        Assert.Equal(32, LauncherTicketToken.Hash(ticket).Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("Abcdefghij123456+/XY")]
    [InlineData("Abcdefghij123456_-XYx")]
    public void RejectsInvalidTicketFormat(string ticket) =>
        Assert.False(LauncherTicketToken.IsValidFormat(ticket));
}
