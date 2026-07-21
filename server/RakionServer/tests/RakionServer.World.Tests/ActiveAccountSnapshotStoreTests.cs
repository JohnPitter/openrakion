using System;
using System.IO;
using RakionServer.Common;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class ActiveAccountSnapshotStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"active-account-test-{Guid.NewGuid():N}.json");

    [Fact]
    public void ContainsMatchesAccountCaseInsensitivelyWithoutPersistingPlainText()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ActiveAccountSnapshotStore.Write(new ActiveAccountSnapshot(
            true, [ActiveAccountSnapshotStore.Hash("ContaTeste")], now), _path);

        Assert.True(ActiveAccountSnapshotStore.Contains(
            "contateste", TimeSpan.FromSeconds(6), now, _path));
        Assert.DoesNotContain("ContaTeste", File.ReadAllText(_path));
    }

    [Fact]
    public void ContainsRejectsStaleOrOfflineSnapshot()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string hash = ActiveAccountSnapshotStore.Hash("test");
        ActiveAccountSnapshotStore.Write(new ActiveAccountSnapshot(
            true, [hash], now.AddSeconds(-7)), _path);
        Assert.False(ActiveAccountSnapshotStore.Contains(
            "test", TimeSpan.FromSeconds(6), now, _path));

        ActiveAccountSnapshotStore.Write(new ActiveAccountSnapshot(false, [hash], now), _path);
        Assert.False(ActiveAccountSnapshotStore.Contains(
            "test", TimeSpan.FromSeconds(6), now, _path));
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
