using System.Security.Cryptography;
using RakionServer.Common;

namespace RakionServer.LauncherWeb;

public sealed class UpdateReleaseProvider
{
    private const string ReadyMarker = "_ready";
    private const string DeleteList = "delete.list";
    private readonly LauncherWebConfig _config;

    public UpdateReleaseProvider(LauncherWebConfig config) => _config = config;

    public async Task<SignedUpdateManifest?> GetLatestAsync(
        int appId, int currentVersion, CancellationToken cancellationToken)
    {
        if (!_config.UpdatesEnabled) return null;
        string? release = FindLatestRelease(appId, currentVersion);
        if (release is null) return null;
        int version = int.Parse(Path.GetFileName(release));
        var files = new List<UpdateFileEntry>();
        foreach (string file in Directory.EnumerateFiles(release, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(file);
            UpdatePathPolicy.RejectReparsePoints(_config.ContentRoot, file);
            string relative = Path.GetRelativePath(release, file).Replace('\\', '/');
            if (relative is ReadyMarker or DeleteList) continue;
            relative = UpdatePathPolicy.NormalizeRelative(relative);
            var info = new FileInfo(file);
            string hash = await Sha256Async(file, cancellationToken);
            files.Add(new UpdateFileEntry(relative, UpdateOperation.Replace,
                info.Length, hash, FileUrl(appId, version, relative)));
        }
        files.AddRange(ReadDeletes(release));
        files.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        EnsureUniquePaths(files);
        DateTimeOffset published = File.GetLastWriteTimeUtc(Path.Combine(release, ReadyMarker));
        var manifest = new UpdateManifest(1, appId, version, published, files);
        string signature = UpdateManifestCodec.Sign(manifest, _config.SigningPrivateKeyPem!);
        return new SignedUpdateManifest(manifest, signature);
    }

    public string ResolveDownload(int appId, int version, string relative)
    {
        if (!_config.UpdatesEnabled) throw new FileNotFoundException("Updates desativados.");
        string release = ReleaseDirectory(appId, version);
        if (!File.Exists(Path.Combine(release, ReadyMarker)))
            throw new FileNotFoundException("Release não publicada.");
        string target = UpdatePathPolicy.ResolveUnderRoot(release, relative);
        RejectReparsePoint(target);
        UpdatePathPolicy.RejectReparsePoints(_config.ContentRoot, target);
        if (!File.Exists(target) || Path.GetFileName(target) is ReadyMarker or DeleteList)
            throw new FileNotFoundException("Arquivo de update não encontrado.");
        return target;
    }

    private string? FindLatestRelease(int appId, int currentVersion)
    {
        string appRoot = Path.Combine(_config.ContentRoot, appId.ToString());
        if (!Directory.Exists(appRoot)) return null;
        return Directory.EnumerateDirectories(appRoot)
            .Select(path => (Path: path, Valid: int.TryParse(Path.GetFileName(path), out int v), Version: v))
            .Where(item => item.Valid && item.Version > currentVersion &&
                File.Exists(Path.Combine(item.Path, ReadyMarker)))
            .OrderByDescending(item => item.Version)
            .Select(item => item.Path)
            .FirstOrDefault();
    }

    private IEnumerable<UpdateFileEntry> ReadDeletes(string release)
    {
        string list = Path.Combine(release, DeleteList);
        if (!File.Exists(list)) yield break;
        foreach (string line in File.ReadLines(list))
        {
            string path = line.Trim();
            if (path.Length == 0 || path.StartsWith('#')) continue;
            yield return new UpdateFileEntry(
                UpdatePathPolicy.NormalizeRelative(path), UpdateOperation.Delete, 0,
                new string('0', 64), null);
        }
    }

    private string ReleaseDirectory(int appId, int version) =>
        Path.Combine(_config.ContentRoot, appId.ToString(), version.ToString());

    private static string FileUrl(int appId, int version, string path) =>
        $"/api/v1/update-files/{appId}/{version}/" + string.Join('/',
            path.Split('/').Select(Uri.EscapeDataString));

    private static async Task<string> Sha256Async(
        string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void EnsureUniquePaths(IEnumerable<UpdateFileEntry> files)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (UpdateFileEntry file in files)
            if (!paths.Add(file.Path))
                throw new InvalidDataException($"Path duplicado no release: {file.Path}");
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Reparse point recusado no release: {path}");
    }
}
