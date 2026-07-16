using System.Collections.Generic;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class CellProgressionTests
{
    private static readonly Dictionary<(int Npc, byte Level), long> Curve = new()
    {
        [(0, 1)] = 0,
        [(0, 2)] = 35,
        [(0, 3)] = 105
    };

    [Fact]
    public void ProjectClampsAwardAndUsesStrictNpcThreshold()
    {
        var current = new EquippedCellState(10, 7, 8000, 8000007, 1, 0);

        CellProgressionChange result = CellProgression.Project(current, 500, Threshold, 9999);

        Assert.Equal(100u, result.AppliedExp);
        Assert.Equal(3, result.After.Level);
        Assert.Equal(100, result.After.Exp);
    }

    [Fact]
    public void ProjectIgnoresEmptyAndNonCellSlots()
    {
        var empty = new EquippedCellState(10, 0, 0, 0, 0, 0);
        var gear = new EquippedCellState(11, 8, 1001, 8000008, 4, 50);

        Assert.Equal(0u, CellProgression.Project(empty, 80, Threshold, 9999).AppliedExp);
        Assert.Equal(0u, CellProgression.Project(gear, 80, Threshold, 9999).AppliedExp);
    }

    [Fact]
    public void ProjectCapsLevelNinetyNineExperience()
    {
        var current = new EquippedCellState(12, 9, 8000, 8000009, 99, 990);

        CellProgressionChange result = CellProgression.Project(current, 100, Threshold, 1000);

        Assert.Equal(99, result.After.Level);
        Assert.Equal(1000, result.After.Exp);
    }

    [Fact]
    public void ProjectAcceptsTrustedStageAwardAboveGamePointCap()
    {
        var current = new EquippedCellState(10, 7, 8000, 8000007, 1, 0);

        CellProgressionChange result = CellProgression.Project(
            current, 256, Threshold, 9999, uint.MaxValue);

        Assert.Equal(256u, result.AppliedExp);
        Assert.Equal(256, result.After.Exp);
    }

    private static long? Threshold(int npc, byte level) =>
        Curve.TryGetValue((npc, level), out long exp) ? exp : null;
}
