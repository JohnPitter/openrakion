using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using RakionServer.Common;

namespace RakionLauncher;

internal sealed class UpdateClient
{
    private const long MaximumReleaseBytes = 8L * 1024 * 1024 * 1024;
    private readonly HttpClient _http;

    public UpdateClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task<int> ApplyLatestAsync(
        string clientRoot, LauncherConfig config, IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        if (!config.UpdatesEnabled) return GetInstalledVersion(clientRoot, config.BaseVersion);
        string keyPath = config.ResolvePublicKeyPath();
        if (!File.Exists(keyPath))
            throw new FileNotFoundException("Chave pública de update não encontrada.", keyPath);

        int currentVersion = GetInstalledVersion(clientRoot, config.BaseVersion);
        using HttpResponseMessage response = await _http.GetAsync(
            new Uri(config.UpdateBaseUrl,
                $"api/v1/updates/{config.AppId}?version={currentVersion}"),
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent) return currentVersion;
        response.EnsureSuccessStatusCode();
        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
        SignedUpdateManifest envelope = await JsonSerializer.DeserializeAsync<SignedUpdateManifest>(
            body, UpdateManifestCodec.JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Manifesto de update vazio.");
        ValidateEnvelope(envelope, config, currentVersion, File.ReadAllText(keyPath));

        progress?.Report($"Baixando atualização {envelope.Manifest.Version}…");
        await DownloadAndApplyAsync(clientRoot, config.UpdateBaseUrl,
            envelope.Manifest, progress, cancellationToken);
        return envelope.Manifest.Version;
    }

    internal static void ValidateEnvelope(
        SignedUpdateManifest envelope, LauncherConfig config, int currentVersion,
        string publicKeyPem)
    {
        UpdateManifest manifest = envelope.Manifest;
        if (manifest.Schema != 1 || manifest.AppId != config.AppId ||
            manifest.Version <= currentVersion || manifest.Files.Count > 10_000)
            throw new InvalidDataException("Cabeçalho do manifesto é inválido.");
        if (!UpdateManifestCodec.Verify(envelope, publicKeyPem))
            throw new CryptographicException("Assinatura do manifesto é inválida.");

        long total = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (UpdateFileEntry file in manifest.Files)
        {
            string path = UpdatePathPolicy.NormalizeRelative(file.Path);
            if (!paths.Add(path) || path.StartsWith(".update/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Path duplicado ou reservado: {path}");
            if (file.Operation == UpdateOperation.Replace)
            {
                if (file.Size < 0 || file.Size > MaximumReleaseBytes ||
                    file.Sha256.Length != 64 || file.Url is null)
                    throw new InvalidDataException($"Entrada de arquivo inválida: {path}");
                total = checked(total + file.Size);
            }
            else if (file.Operation != UpdateOperation.Delete || file.Url is not null)
                throw new InvalidDataException($"Operação de update inválida: {path}");
        }
        if (total > MaximumReleaseBytes)
            throw new InvalidDataException("Release excede o limite de tamanho.");
    }

    private async Task DownloadAndApplyAsync(
        string clientRoot, Uri baseUrl, UpdateManifest manifest,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        string stateRoot = Path.Combine(clientRoot, ".update");
        Directory.CreateDirectory(stateRoot);
        await using var updateLock = new FileStream(Path.Combine(stateRoot, "update.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string staging = Path.Combine(stateRoot, "staging", manifest.Version.ToString());
        string backup = Path.Combine(stateRoot, "backup", manifest.Version.ToString());
        DeleteTree(staging, stateRoot);
        DeleteTree(backup, stateRoot);
        Directory.CreateDirectory(staging);
        bool completed = false;

        try
        {
            foreach (UpdateFileEntry file in manifest.Files.Where(
                entry => entry.Operation == UpdateOperation.Replace))
                await DownloadAsync(baseUrl, staging, file, progress, cancellationToken);
            ApplyTransaction(clientRoot, staging, backup, manifest.Files,
                () => WriteVersion(clientRoot, manifest.Version));
            completed = true;
        }
        finally
        {
            DeleteTree(staging, stateRoot);
            if (completed) DeleteTree(backup, stateRoot);
        }
    }

    private async Task DownloadAsync(
        Uri baseUrl, string staging, UpdateFileEntry file,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Uri url = ResolveSameOrigin(baseUrl, file.Url!);
        string target = UpdatePathPolicy.ResolveUnderRoot(staging, file.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        progress?.Report($"Baixando {file.Path}…");
        using HttpResponseMessage response = await _http.GetAsync(url,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            written = checked(written + read);
            if (written > file.Size) throw new InvalidDataException($"Download excedeu o tamanho: {file.Path}");
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (written != file.Size || !CryptographicOperations.FixedTimeEquals(
            hash.GetHashAndReset(), Convert.FromHexString(file.Sha256)))
            throw new InvalidDataException($"Tamanho ou SHA-256 inválido: {file.Path}");
    }

    private static Uri ResolveSameOrigin(Uri baseUrl, string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out _))
            throw new InvalidDataException("URL absoluta não é aceita no manifesto.");
        var resolved = new Uri(baseUrl, value);
        if (resolved.Scheme != baseUrl.Scheme || resolved.Host != baseUrl.Host ||
            resolved.Port != baseUrl.Port)
            throw new InvalidDataException("Download saiu da origem do manifesto.");
        return resolved;
    }

    internal static int GetInstalledVersion(string root, int baseVersion)
    {
        string path = Path.Combine(root, ".update", "version");
        return File.Exists(path) && int.TryParse(File.ReadAllText(path), out int version)
            ? Math.Max(baseVersion, version)
            : baseVersion;
    }

    private static void WriteVersion(string root, int version)
    {
        string directory = Path.Combine(root, ".update");
        Directory.CreateDirectory(directory);
        string pending = Path.Combine(directory, "version.pending");
        File.WriteAllText(pending, version.ToString());
        File.Move(pending, Path.Combine(directory, "version"), true);
    }

    private static void DeleteTree(string target, string stateRoot)
    {
        string root = Path.GetFullPath(stateRoot).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(target);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Limpeza de update escapou da raiz de estado.");
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }

    private static void ApplyTransaction(
        string clientRoot, string staging, string backup,
        IReadOnlyList<UpdateFileEntry> files, Action commit) =>
        UpdateTransaction.Apply(clientRoot, staging, backup, files, commit);
}
