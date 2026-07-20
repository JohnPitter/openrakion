using RakionServer.Common;

namespace RakionServer.LauncherWeb;

public sealed record PublicServerStatus(bool Online, int OnlinePlayers, int Capacity);

public static class ServerStatusEndpoints
{
    private static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(6);

    public static void MapServerStatus(this WebApplication app)
    {
        app.MapGet("/api/v1/server-status", () =>
        {
            ServerStatusSnapshot? snapshot = ServerStatusSnapshotStore.ReadFresh(
                MaximumAge, DateTimeOffset.UtcNow);
            return Results.Json(new PublicServerStatus(
                snapshot?.Online == true,
                snapshot?.Online == true ? snapshot.OnlinePlayers : 0,
                snapshot?.Capacity ?? 0));
        });
    }
}
