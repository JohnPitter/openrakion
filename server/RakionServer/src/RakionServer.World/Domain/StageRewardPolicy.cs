using System;
using System.Collections.Generic;

namespace RakionServer.World.Domain;

public readonly record struct StageReward(uint Exp, uint Gold);

public static class StageRewardPolicy
{
    public static StageReward Calculate(
        StageContentDefinition stage, byte rank, byte previousBestRank)
    {
        if (rank is < 1 or > 5 || rank <= previousBestRank)
            return default;

        StageRankDefinition current = FindRank(stage.Ranks, rank);
        if (previousBestRank == 0)
            return new StageReward(current.Exp, current.Gold);

        StageRankDefinition previous = FindRank(stage.Ranks, previousBestRank);
        return new StageReward(
            current.Exp > previous.Exp ? current.Exp - previous.Exp : 0,
            current.Gold > previous.Gold ? current.Gold - previous.Gold : 0);
    }

    public static uint CellExp(uint stageExp, bool equipped, bool expBonusActive)
    {
        if (!equipped) return 0;
        uint value = stageExp / 3;
        return expBonusActive ? checked(value + value / 2) : value;
    }

    private static StageRankDefinition FindRank(
        IReadOnlyList<StageRankDefinition> ranks, byte rank)
    {
        foreach (StageRankDefinition item in ranks)
            if (item.Rank == rank) return item;
        throw new ArgumentException($"Rank {rank} ausente no catálogo do stage.");
    }
}

public static class StageRankPolicy
{
    public static byte Calculate(StageContentDefinition stage, uint metric)
    {
        bool lowerIsBetter = stage.Goal == "time attack";
        if (!lowerIsBetter && stage.Goal is not ("butchery" or "survival" or "guard"))
            throw new ArgumentException($"Goal de stage desconhecido: {stage.Goal}.");

        foreach (StageRankDefinition rank in stage.Ranks)
            if (lowerIsBetter ? metric <= rank.Threshold : metric >= rank.Threshold)
                return rank.Rank;
        return 0;
    }

    public static uint TruncatePercentage(float current, float total) =>
        current <= 0 || total <= 0 ? 0 : checked((uint)(current / total * 100f));
}
