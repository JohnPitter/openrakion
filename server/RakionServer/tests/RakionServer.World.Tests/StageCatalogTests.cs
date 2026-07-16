using System;
using System.Collections.Generic;
using System.Linq;
using RakionServer.World.Domain;
using RakionServer.World.Infrastructure;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class StageCatalogTests
{
    [Fact]
    public void CatalogEnforcesStageLevelAndPartyLimits()
    {
        var catalog = new StageCatalog([
            new StageDefinition(3, 1, 2, 13),
            new StageDefinition(6, 3, 3, 14)]);

        Assert.True(catalog.CanEnter(3, 2, 1, false));
        Assert.False(catalog.CanEnter(3, 1, 1, false));
        Assert.False(catalog.CanEnter(3, 2, 2, false));
        Assert.True(catalog.CanEnter(3, 1, 1, true));
        Assert.False(catalog.CanEnter(49, 10, 1, true));
    }

    [Fact]
    public void CatalogRejectsInvalidOrDuplicateDefinitions()
    {
        Assert.Throws<ArgumentException>(() => new StageCatalog([
            new StageDefinition(1, 0, 1, 10)]));
        Assert.Throws<ArgumentException>(() => new StageCatalog([
            new StageDefinition(1, 1, 1, 10),
            new StageDefinition(1, 1, 1, 10)]));
    }

    [Fact]
    public void EmbeddedV258CatalogMatchesThe48ActiveStages()
    {
        IReadOnlyList<StageContentDefinition> content = StageContentLoader.LoadEmbedded();
        StageDefinition[] access = content.Select(stage => new StageDefinition(
            stage.Id, stage.MaxPlayers, stage.MinLevel, stage.MaxLevel)).ToArray();
        var catalog = new StageCatalog(access, content);

        Assert.Equal(48, catalog.Count);
        Assert.True(catalog.TryGetContent(3, out StageContentDefinition? stage3));
        Assert.NotNull(stage3);
        Assert.Equal((ushort)288, stage3.TimeLimitSeconds);
        Assert.Equal("time attack", stage3.Goal);
        Assert.Equal((uint)40, stage3.Ranks.Single(rank => rank.Rank == 4).Exp);
        Assert.Equal((uint)83, stage3.Ranks.Single(rank => rank.Rank == 4).Gold);
        Assert.Equal(114, stage3.FlowNodeCount);
        Assert.Equal(61, stage3.ReachableFlowNodeCount);
        Assert.True(stage3.FlowReferencesConsistent);
        Assert.Equal([28, 38], content
            .Where(stage => !stage.RankThresholdsConsistent)
            .Select(stage => (int)stage.Id).ToArray());
        Assert.Equal([8, 15, 17, 20, 23, 25, 26, 35, 36, 40, 41, 42, 45, 46], content
            .Where(stage => !stage.FlowReferencesConsistent)
            .Select(stage => (int)stage.Id).ToArray());
        Assert.Equal([7, 8, 11, 14, 16, 17, 19, 20, 22, 29], content
            .Where(stage => !stage.FlowNamesUnique)
            .Select(stage => (int)stage.Id).ToArray());
    }

    [Fact]
    public void CatalogRejectsDivergenceBetweenDatabaseAndLevelData()
    {
        StageContentDefinition content = StageContentLoader.LoadEmbedded().First();
        var divergent = new StageDefinition(
            content.Id, 4, content.MinLevel, content.MaxLevel);

        Assert.Throws<ArgumentException>(() => new StageCatalog([divergent], [content]));
    }

    [Fact]
    public void LevelFreeUsesSameMinuteEpochAndExpiresAfterOneDay()
    {
        var purchased = new DateTime(2026, 7, 15, 10, 30, 0, DateTimeKind.Local);
        long marker = StageLevelFreePolicy.CurrentMinuteMarker(purchased);

        Assert.True(StageLevelFreePolicy.IsActive(marker, purchased.AddMinutes(1440)));
        Assert.False(StageLevelFreePolicy.IsActive(marker, purchased.AddMinutes(1441)));
        Assert.False(StageLevelFreePolicy.IsActive(0, purchased));
    }
}
