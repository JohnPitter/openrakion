using System.Collections.Generic;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class CharacterProgressionTests
{
    [Theory]
    [InlineData(100, 20, 0, 90)]
    [InlineData(100, 20, 3, 75)]
    [InlineData(10, 20, 0, 0)]
    public void CombatExitPenaltyUsesCharacterLevelAndExperienceOffsets(
        long currentExp, byte level, byte differential, uint expected)
    {
        Assert.Equal(expected,
            CharacterProgression.ApplyCombatExitPenalty(currentExp, level, differential));
    }

    private static readonly Dictionary<byte, int> Curve = new()
    {
        [0] = 0,
        [1] = 100,
        [2] = 300,
        [3] = 600
    };

    [Fact]
    public void ProjectUsesTwoFifthsThresholdAndCanAdvanceMultipleLevels()
    {
        CharacterProgressionState result = CharacterProgression.Project(
            new CharacterProgressionState(0, 1, 0), 420, LevelExp);

        Assert.Equal(new CharacterProgressionState(420, 4, 9), result);
    }

    [Fact]
    public void ProjectDoesNotMutateLevelBelowThreshold()
    {
        CharacterProgressionState result = CharacterProgression.Project(
            new CharacterProgressionState(39, 1, 7), 0, LevelExp);

        Assert.Equal(new CharacterProgressionState(39, 1, 7), result);
    }

    private static int LevelExp(byte level) => Curve.TryGetValue(level, out int exp) ? exp : 0;
}
