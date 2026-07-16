using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace RakionServer.World.Domain;

public readonly record struct StageDefinition(
    byte Id, byte MaxCharacters, byte MinLevel, byte MaxLevel);

public sealed class StageCatalog
{
    private readonly IReadOnlyDictionary<byte, StageDefinition> _stages;
    private readonly IReadOnlyDictionary<byte, StageContentDefinition> _content;

    public StageCatalog(
        IEnumerable<StageDefinition> stages,
        IEnumerable<StageContentDefinition>? content = null)
    {
        var values = new Dictionary<byte, StageDefinition>();
        foreach (StageDefinition stage in stages)
        {
            if (stage.Id == 0 || stage.MaxCharacters is < 1 or > 4 ||
                stage.MinLevel == 0 || stage.MaxLevel < stage.MinLevel)
                throw new ArgumentException($"Stage {stage.Id} possui limites inválidos.");
            if (!values.TryAdd(stage.Id, stage))
                throw new ArgumentException($"Stage {stage.Id} está duplicado.");
        }
        _stages = values;
        _content = BuildContent(values, content ?? Array.Empty<StageContentDefinition>());
    }

    public int Count => _stages.Count;

    public bool TryGet(byte stageId, out StageDefinition stage) =>
        _stages.TryGetValue(stageId, out stage);

    public bool TryGetContent(
        byte stageId, [NotNullWhen(true)] out StageContentDefinition? content) =>
        _content.TryGetValue(stageId, out content);

    public bool CanEnter(byte stageId, byte level, int players, bool levelFree)
    {
        if (!TryGet(stageId, out StageDefinition stage) ||
            players < 1 || players > stage.MaxCharacters)
            return false;
        return levelFree || level >= stage.MinLevel && level <= stage.MaxLevel;
    }

    private static IReadOnlyDictionary<byte, StageContentDefinition> BuildContent(
        IReadOnlyDictionary<byte, StageDefinition> stages,
        IEnumerable<StageContentDefinition> content)
    {
        var values = new Dictionary<byte, StageContentDefinition>();
        foreach (StageContentDefinition item in content)
        {
            ValidateContent(item);
            if (!values.TryAdd(item.Id, item))
                throw new ArgumentException($"Conteúdo do stage {item.Id} está duplicado.");
            if (!stages.TryGetValue(item.Id, out StageDefinition stage))
                throw new ArgumentException($"Conteúdo do stage {item.Id} não existe em stageinfo.");
            if (stage.MaxCharacters != item.MaxPlayers || stage.MinLevel != item.MinLevel ||
                stage.MaxLevel != item.MaxLevel)
                throw new ArgumentException($"Stage {item.Id} diverge entre stageinfo e LevelData.");
        }
        if (values.Count > 0 && values.Count != stages.Count)
            throw new ArgumentException(
                $"Catálogos de stage incompletos: stageinfo={stages.Count}, LevelData={values.Count}.");
        return values;
    }

    private static void ValidateContent(StageContentDefinition item)
    {
        if (item.Id == 0 || item.MapId == 0 || item.TimeLimitSeconds == 0 ||
            item.MinPlayers == 0 || item.MaxPlayers is < 1 or > 4 ||
            item.MinPlayers > item.MaxPlayers || item.MinLevel == 0 ||
            item.MaxLevel < item.MinLevel || item.Ranks.Count != 5 ||
            item.SpawnDefinitionCount < 0 || item.NpcCount < 0 ||
            item.FlowNodeCount < 0 || item.ReachableFlowNodeCount < 0 ||
            item.ReachableFlowNodeCount > item.FlowNodeCount)
            throw new ArgumentException($"Conteúdo do stage {item.Id} possui limites inválidos.");
        var ranks = new HashSet<byte>();
        foreach (StageRankDefinition rank in item.Ranks)
        {
            if (rank.Rank is < 1 or > 5 || rank.Multiplier <= 0 ||
                !ranks.Add(rank.Rank))
                throw new ArgumentException($"Ranks do stage {item.Id} são inválidos.");
        }
    }
}

public static class StageLevelFreePolicy
{
    public const long DurationMinutes = 1440;

    public static long CurrentMinuteMarker(DateTime value)
    {
        DateTime local = value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
        long days = DateOnly.FromDateTime(local).DayNumber + 366L;
        return checked(days * 1440L + local.Hour * 60L + local.Minute);
    }

    public static bool IsActive(long marker, DateTime now) =>
        marker > 0 && marker + DurationMinutes >= CurrentMinuteMarker(now);
}
