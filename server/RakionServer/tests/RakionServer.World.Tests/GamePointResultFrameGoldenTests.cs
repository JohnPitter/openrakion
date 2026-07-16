using System;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class GamePointResultFrameGoldenTests
{
    [Fact]
    public void ResultUsesOriginalTypeAndSixtyByteBody()
    {
        var snapshot = new GamePointResultSnapshot(
            0x11223344, 25, 0x55667788,
            new CharacterProgressionState(0x12345678, 7, 9), 10, 11, 12,
            [new(101, 1, 201), new(102, 2, 202), new(103, 3, 203)]);

        byte[] body = GamePointResultFrames.Build(snapshot);

        Assert.Equal((ushort)0x0a, GamePointResultFrames.MessageType);
        Assert.Equal(60, body.Length);
        Assert.Equal(
            "44332211190000008877665507785634120A0000000B0000000C0000000900" +
            "650000006600000067000000010203C9000000CA000000CB0000000000",
            Convert.ToHexString(body));
    }
}
