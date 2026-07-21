using RakionServer.Common;

namespace RakionServer.LauncherWeb;

public sealed class ActiveAccountLookup
{
    private static readonly TimeSpan SnapshotMaximumAge = TimeSpan.FromSeconds(6);
    private readonly string? _snapshotPath;

    public ActiveAccountLookup(string? snapshotPath = null) => _snapshotPath = snapshotPath;

    public bool Contains(string account) => ActiveAccountSnapshotStore.Contains(
        account, SnapshotMaximumAge, DateTimeOffset.UtcNow, _snapshotPath);
}
