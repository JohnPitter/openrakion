using System;
using System.IO;
using System.Text.Json;

namespace RakionServer.Common
{
    public sealed record ServerStatusSnapshot(
        bool Online, int OnlinePlayers, int Capacity, DateTimeOffset UpdatedAtUtc);

    public static class ServerStatusSnapshotStore
    {
        private const string EnvironmentPath = "RAKION_SERVER_STATUS_PATH";
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public static string DefaultPath => Environment.GetEnvironmentVariable(EnvironmentPath)
            ?? Path.Combine(Path.GetTempPath(), "openrakion-server-status.json");

        public static void Write(ServerStatusSnapshot snapshot, string? path = null)
        {
            string destination = Path.GetFullPath(path ?? DefaultPath);
            string? directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = destination + "." + Environment.ProcessId + "." +
                Guid.NewGuid().ToString("N") + ".tmp";
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

        public static ServerStatusSnapshot? ReadFresh(
            TimeSpan maximumAge, DateTimeOffset now, string? path = null)
        {
            try
            {
                string json = File.ReadAllText(Path.GetFullPath(path ?? DefaultPath));
                ServerStatusSnapshot? snapshot =
                    JsonSerializer.Deserialize<ServerStatusSnapshot>(json, JsonOptions);
                if (snapshot == null || now - snapshot.UpdatedAtUtc > maximumAge) return null;
                return snapshot;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (JsonException) { return null; }
        }
    }
}
