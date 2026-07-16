using System;
using System.Linq;
using RakionServer.Ranking;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class RankingRulesTests
{
    [Fact]
    public void TotalRankIsCompetitionRankPerCountryAndClassRankIsGlobal()
    {
        CharacterRank[] result = RankingRules.RankCharacters(new[]
        {
            Character(1, country: 1, characterClass: 0, experience: 40_000),
            Character(2, country: 1, characterClass: 0, experience: 40_000),
            Character(3, country: 1, characterClass: 0, experience: 35_000),
            Character(4, country: 2, characterClass: 0, experience: 38_000),
            Character(5, country: 1, characterClass: 1, experience: 39_000)
        }).OrderBy(row => row.Source.Id).ToArray();

        Assert.Equal(new[] { 1, 1, 4, 1, 3 }, result.Select(row => row.TotalRank));
        Assert.Equal(new[] { 1, 1, 4, 3, 1 }, result.Select(row => row.ClassRank));
    }

    [Theory]
    [InlineData(0, 26)]
    [InlineData(999, 26)]
    [InlineData(1_000, 25)]
    [InlineData(27_999, 16)]
    [InlineData(28_000, 15)]
    [InlineData(31_999, 15)]
    public void FixedGradesMatchOriginalBoundaries(int experience, byte expected)
    {
        Assert.Equal(expected, RankingRules.FixedGrade(experience));
    }

    [Fact]
    public void RankedGradesPreserveOriginalTopBucketsAndSmallPopulationBehavior()
    {
        byte[] grades = RankingRules.RankedGrades(23);

        Assert.Equal(1, grades.Count(value => value == 1));
        Assert.Equal(4, grades.Count(value => value == 2));
        Assert.Equal(16, grades.Count(value => value == 3));
        Assert.Equal(1, grades.Count(value => value == 4));
        Assert.Equal(1, grades.Count(value => value == 5));
    }

    [Fact]
    public void RankedGradesMatchOriginalCumulativeIntegerBuckets()
    {
        byte[] grades = RankingRules.RankedGrades(1_000);

        Assert.Equal(
            new[] { 1, 4, 16, 1, 9, 20, 29, 39, 69, 98, 127, 157, 196, 234 },
            Enumerable.Range(1, 14).Select(grade => grades.Count(value => value == grade)));
    }

    [Fact]
    public void ClanMemberRankResetsPerClanAndSharesTies()
    {
        ClanMemberRank[] result = RankingRules.RankClanMembers(new[]
        {
            new ClanMemberRankSource(3, 7, 50),
            new ClanMemberRankSource(1, 7, 100),
            new ClanMemberRankSource(2, 7, 100),
            new ClanMemberRankSource(4, 8, 1)
        }).OrderBy(row => row.UserGameId).ToArray();

        Assert.Equal(new[] { 1, 1, 3, 1 }, result.Select(row => row.Rank));
    }

    [Fact]
    public void ClanRankIsGlobalCompetitionRank()
    {
        ClanRank[] result = RankingRules.RankClans(new[]
        {
            Clan(3, 50),
            Clan(1, 100),
            Clan(2, 100)
        });

        Assert.Equal(new[] { 1, 1, 3 }, result.Select(row => row.Rank));
        Assert.Equal(new[] { 1, 2, 3 }, result.Select(row => row.Source.Id));
    }

    private static CharacterRankSource Character(int id, short country, byte characterClass, int experience)
        => new(id, $"user{id}", $"char{id}", 1, characterClass, experience, 0, 0, 0, 0, 0, country);

    private static ClanRankSource Clan(int id, int points)
        => new(id, $"clan{id}", $"master{id}", 1, DateTime.UnixEpoch, points, 0, 1);
}
