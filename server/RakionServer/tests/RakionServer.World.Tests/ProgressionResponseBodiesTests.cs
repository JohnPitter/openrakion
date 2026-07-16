using System;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class ProgressionResponseBodiesTests
{
    [Fact]
    public void LevelUpMatchesClientConsumerLayout()
    {
        byte[] body = ProgressionResponseBodies.LevelUp(0x34, 0x5678);

        Assert.Equal(ProgressionResponseBodies.LevelUpLength, body.Length);
        Assert.Equal(new byte[] { 0x34, 0x78, 0x56 }, body);
    }

    [Fact]
    public void FieldLevelsMatchesClientConsumerLayout()
    {
        byte[] body = ProgressionResponseBodies.FieldLevels(
            0x09, 0x34, new byte[] { 0x10, 0x20, 0x30 });

        Assert.Equal(ProgressionResponseBodies.FieldLevelsLength, body.Length);
        Assert.Equal(new byte[] { 0x09, 0x34, 0x10, 0x20, 0x30 }, body);
    }

    [Fact]
    public void FieldLevelsRejectsWrongCellCount()
    {
        Assert.Throws<ArgumentException>(() =>
            ProgressionResponseBodies.FieldLevels(1, 2, new byte[] { 3, 4 }));
    }
}
