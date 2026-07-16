using System;
using System.Collections.Generic;
using System.Linq;

namespace RakionServer.Ranking;

public static class RankingRules
{
    public const int RankedExperienceMinimum = 32_000;

    public static CharacterRank[] RankCharacters(IReadOnlyCollection<CharacterRankSource> source)
    {
        var totalRanks = new Dictionary<int, int>(source.Count);
        var grades = new Dictionary<int, byte>(source.Count);
        foreach (IGrouping<short, CharacterRankSource> country in source.GroupBy(row => row.Country))
        {
            CharacterRankSource[] ordered = country
                .OrderByDescending(row => row.Experience).ThenBy(row => row.Id).ToArray();
            AssignCompetitionRanks(ordered, row => row.Experience, row => row.Id, totalRanks);
            AssignGrades(ordered, grades);
        }

        var classRanks = new Dictionary<int, int>(source.Count);
        foreach (IGrouping<byte, CharacterRankSource> characterClass in source.GroupBy(row => row.Class))
        {
            CharacterRankSource[] ordered = characterClass
                .OrderByDescending(row => row.Experience).ThenBy(row => row.Id).ToArray();
            AssignCompetitionRanks(ordered, row => row.Experience, row => row.Id, classRanks);
        }

        return source.Select(row => new CharacterRank(
            row, totalRanks[row.Id], classRanks[row.Id], grades[row.Id])).ToArray();
    }

    public static ClanMemberRank[] RankClanMembers(IReadOnlyCollection<ClanMemberRankSource> source)
    {
        var result = new List<ClanMemberRank>(source.Count);
        foreach (IGrouping<int, ClanMemberRankSource> clan in source.GroupBy(row => row.ClanId))
        {
            ClanMemberRankSource[] ordered = clan
                .OrderByDescending(row => row.ClanPoints).ThenBy(row => row.UserGameId).ToArray();
            int previousPoints = 0;
            int rank = 0;
            for (int index = 0; index < ordered.Length; index++)
            {
                if (index == 0 || ordered[index].ClanPoints != previousPoints)
                    rank = index + 1;
                previousPoints = ordered[index].ClanPoints;
                result.Add(new ClanMemberRank(ordered[index].UserGameId, rank));
            }
        }
        return result.ToArray();
    }

    public static ClanRank[] RankClans(IReadOnlyCollection<ClanRankSource> source)
    {
        ClanRankSource[] ordered = source
            .OrderByDescending(row => row.Points).ThenBy(row => row.Id).ToArray();
        var result = new ClanRank[ordered.Length];
        int previousPoints = 0;
        int rank = 0;
        for (int index = 0; index < ordered.Length; index++)
        {
            if (index == 0 || ordered[index].Points != previousPoints)
                rank = index + 1;
            previousPoints = ordered[index].Points;
            result[index] = new ClanRank(ordered[index], rank);
        }
        return result;
    }

    public static byte FixedGrade(int experience) => experience switch
    {
        < 1_000 => 26,
        < 2_500 => 25,
        < 4_000 => 24,
        < 6_000 => 23,
        < 8_000 => 22,
        < 10_500 => 21,
        < 13_000 => 20,
        < 17_000 => 19,
        < 20_000 => 18,
        < 24_000 => 17,
        < 28_000 => 16,
        < RankedExperienceMinimum => 15,
        _ => throw new ArgumentOutOfRangeException(nameof(experience))
    };

    public static byte[] RankedGrades(int population)
    {
        if (population < 0)
            throw new ArgumentOutOfRangeException(nameof(population));

        var result = new byte[population];
        int grade = 0;
        int remainingInGrade = 0;
        int assignedAfterTop21 = 0;
        for (int index = 0; index < population; index++)
        {
            if (remainingInGrade == 0)
                AdvanceOriginalGrade(population, ref grade, ref remainingInGrade, ref assignedAfterTop21);
            result[index] = (byte)grade;
            remainingInGrade--;
        }
        return result;
    }

    private static void AssignGrades(CharacterRankSource[] ordered, Dictionary<int, byte> grades)
    {
        CharacterRankSource[] ranked = ordered.Where(row => row.Experience >= RankedExperienceMinimum).ToArray();
        byte[] rankedGrades = RankedGrades(ranked.Length);
        for (int index = 0; index < ranked.Length; index++)
            grades[ranked[index].Id] = rankedGrades[index];
        foreach (CharacterRankSource row in ordered.Where(row => row.Experience < RankedExperienceMinimum))
            grades[row.Id] = FixedGrade(row.Experience);
    }

    private static void AssignCompetitionRanks<T>(
        T[] ordered,
        Func<T, int> score,
        Func<T, int> id,
        Dictionary<int, int> destination)
    {
        int previousScore = 0;
        int rank = 0;
        for (int index = 0; index < ordered.Length; index++)
        {
            int currentScore = score(ordered[index]);
            if (index == 0 || currentScore != previousScore)
                rank = index + 1;
            previousScore = currentScore;
            destination[id(ordered[index])] = rank;
        }
    }

    private static void AdvanceOriginalGrade(
        int population,
        ref int grade,
        ref int remaining,
        ref int assignedAfterTop21)
    {
        int remainder = population - 21;
        grade++;
        remaining = grade switch
        {
            1 => 1,
            2 => 4,
            3 => 16,
            4 => SetCumulative(remainder / 1_000 + 1, ref assignedAfterTop21),
            5 => SetCumulative(remainder / 100 + 1, ref assignedAfterTop21),
            6 => SetCumulative((population * 3 - 63) / 100 + 1, ref assignedAfterTop21),
            7 => SetCumulative((population * 6 - 126) / 100 + 1, ref assignedAfterTop21),
            8 => SetCumulative(remainder / 10 + 1, ref assignedAfterTop21),
            9 => SetCumulative((population * 17 - 357) / 100 + 1, ref assignedAfterTop21),
            10 => SetCumulative((population * 27 - 567) / 100 + 1, ref assignedAfterTop21),
            11 => SetCumulative((population * 40 - 840) / 100 + 1, ref assignedAfterTop21),
            12 => SetCumulative((population * 56 - 1_176) / 100 + 1, ref assignedAfterTop21),
            13 => SetCumulative((population * 76 - 1_596) / 100 + 1, ref assignedAfterTop21),
            14 => -1,
            _ => throw new InvalidOperationException("Estado de grade original inválido.")
        };
    }

    private static int SetCumulative(int target, ref int assigned)
    {
        int count = target - assigned;
        assigned += count;
        return count;
    }
}
