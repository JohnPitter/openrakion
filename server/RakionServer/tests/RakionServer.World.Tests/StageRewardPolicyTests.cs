using System.Collections.Generic;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class StageRewardPolicyTests
{
    private static readonly StageContentDefinition Stage = new(
        3, "stage_003.txt", new string('a', 64), 3, 288, "time attack", null,
        1, 1, 2, 13, true,
        new List<StageRankDefinition>
        {
            new(5, 96, 64, 132, 4m), new(4, 128, 40, 83, 2.5m),
            new(3, 160, 32, 66, 2m), new(2, 224, 24, 50, 1.5m),
            new(1, 288, 16, 33, 1m)
        }, 1, 1);

    [Fact]
    public void FirstClearUsesFullCatalogReward()
    {
        Assert.Equal(new StageReward(40, 83), StageRewardPolicy.Calculate(Stage, 4, 0));
    }

    [Fact]
    public void ImprovedRankUsesOnlyDifference()
    {
        Assert.Equal(new StageReward(8, 17), StageRewardPolicy.Calculate(Stage, 4, 3));
        Assert.Equal(default, StageRewardPolicy.Calculate(Stage, 3, 4));
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 13)]
    [InlineData(true, true, 19)]
    public void CellExpMatchesProducer(bool equipped, bool bonus, uint expected)
    {
        Assert.Equal(expected, StageRewardPolicy.CellExp(40, equipped, bonus));
    }

    [Theory]
    [InlineData(96, 5)]
    [InlineData(97, 4)]
    [InlineData(288, 1)]
    [InlineData(289, 0)]
    public void TimeAttackUsesFirstUpperThreshold(uint seconds, byte expected)
    {
        Assert.Equal(expected, StageRankPolicy.Calculate(Stage, seconds));
    }

    [Fact]
    public void ButcheryAndPercentageGoalsUseFirstLowerThreshold()
    {
        StageContentDefinition butchery = Stage with
        {
            Goal = "butchery",
            Ranks = new List<StageRankDefinition>
            {
                new(5, 100, 1, 1, 4m), new(4, 70, 1, 1, 3m),
                new(3, 60, 1, 1, 2m), new(2, 50, 1, 1, 1.5m),
                new(1, 40, 1, 1, 1m)
            }
        };

        Assert.Equal(5, StageRankPolicy.Calculate(butchery, 100));
        Assert.Equal(4, StageRankPolicy.Calculate(butchery, 70));
        Assert.Equal(0, StageRankPolicy.Calculate(butchery, 39));
        Assert.Equal(79u, StageRankPolicy.TruncatePercentage(79.9f, 100f));
    }
}
