using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RakionServer.Common;

public sealed record ActiveAccountSnapshot(
    bool Online, string[] AccountHashes, DateTimeOffset UpdatedAtUtc);

public static class ActiveAccountSnapshotStore
{
    private const string EnvironmentPath = "RAKION_ACTIVE_ACCOUNTS_PATH";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string DefaultPath => Environment.GetEnvironmentVariable(EnvironmentPath)
        ?? Path.Combine(Path.GetTempPath(), "openrakion-active-accounts.json");

    public static void Write(ActiveAccountSnapshot snapshot, string? path = null)
    {
        string destination = Path.GetFullPath(path ?? DefaultPath);
        string? directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporary = $"{destination}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static bool Contains(
        string account, TimeSpan maximumAge, DateTimeOffset now, string? path = null)
        => FilterOnline([account], maximumAge, now, path).Length != 0;

    public static string[] FilterOnline(
        IEnumerable<string> accounts, TimeSpan maximumAge, DateTimeOffset now,
        string? path = null)
    {
        try
        {
            string json = File.ReadAllText(Path.GetFullPath(path ?? DefaultPath));
            ActiveAccountSnapshot? snapshot =
                JsonSerializer.Deserialize<ActiveAccountSnapshot>(json, JsonOptions);
            if (snapshot?.Online != true || now - snapshot.UpdatedAtUtc > maximumAge)
                return [];
            var online = snapshot.AccountHashes.ToHashSet(StringComparer.Ordinal);
            return accounts.Where(account => online.Contains(Hash(account))).ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
        catch (JsonException) { return []; }
    }

    public static string Hash(string account) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(account.Trim().ToUpperInvariant())));
}
