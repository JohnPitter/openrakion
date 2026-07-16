using System.Collections.Generic;

namespace RakionServer.World.Domain;

public sealed record StageRankDefinition(
    byte Rank, uint Threshold, uint Exp, uint Gold, decimal Multiplier);

public sealed record StageContentDefinition(
    byte Id,
    string SourceFile,
    string SourceSha256,
    byte MapId,
    ushort TimeLimitSeconds,
    string Goal,
    string? GoalArgument,
    byte MinPlayers,
    byte MaxPlayers,
    byte MinLevel,
    byte MaxLevel,
    bool RankThresholdsConsistent,
    IReadOnlyList<StageRankDefinition> Ranks,
    int SpawnDefinitionCount,
    int NpcCount,
    int FlowNodeCount = 0,
    int ReachableFlowNodeCount = 0,
    bool FlowReferencesConsistent = true,
    bool FlowNamesUnique = true);
