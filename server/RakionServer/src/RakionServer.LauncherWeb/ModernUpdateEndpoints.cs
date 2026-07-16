using RakionServer.Common;

namespace RakionServer.LauncherWeb;

public static class ModernUpdateEndpoints
{
    public static void MapModernUpdates(this WebApplication app)
    {
        app.MapGet("/api/v1/updates/{appId:int}", async (
            int appId, int version, UpdateReleaseProvider releases,
            CancellationToken cancellationToken) =>
        {
            SignedUpdateManifest? manifest = await releases.GetLatestAsync(
                appId, version, cancellationToken);
            return manifest is null ? Results.NoContent() : Results.Json(
                manifest, UpdateManifestCodec.JsonOptions);
        });

        app.MapGet("/api/v1/update-files/{appId:int}/{version:int}/{**path}", (
            int appId, int version, string path, UpdateReleaseProvider releases) =>
        {
            try
            {
                string file = releases.ResolveDownload(appId, version, path);
                return Results.File(file, "application/octet-stream", enableRangeProcessing: true);
            }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return Results.NotFound();
            }
        });
    }
}
