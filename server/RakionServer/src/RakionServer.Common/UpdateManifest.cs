using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace RakionServer.Common;

public enum UpdateOperation
{
    Replace = 0,
    Delete = 1
}

public sealed record UpdateFileEntry(
    string Path, UpdateOperation Operation, long Size, string Sha256, string? Url);

public sealed record UpdateManifest(
    int Schema, int AppId, int Version, DateTimeOffset PublishedAt,
    IReadOnlyList<UpdateFileEntry> Files);

public sealed record SignedUpdateManifest(UpdateManifest Manifest, string Signature);

public static class UpdateManifestCodec
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static byte[] CanonicalBytes(UpdateManifest manifest)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema", manifest.Schema);
            writer.WriteNumber("appId", manifest.AppId);
            writer.WriteNumber("version", manifest.Version);
            writer.WriteString("publishedAt", manifest.PublishedAt.UtcDateTime.ToString("O"));
            writer.WriteStartArray("files");
            foreach (UpdateFileEntry file in manifest.Files)
            {
                writer.WriteStartObject();
                writer.WriteString("path", file.Path);
                writer.WriteNumber("operation", (int)file.Operation);
                writer.WriteNumber("size", file.Size);
                writer.WriteString("sha256", file.Sha256.ToLowerInvariant());
                if (file.Url is null) writer.WriteNull("url");
                else writer.WriteString("url", file.Url);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static string Sign(UpdateManifest manifest, string privateKeyPem)
    {
        using ECDsa key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return Convert.ToBase64String(key.SignData(
            CanonicalBytes(manifest), HashAlgorithmName.SHA256));
    }

    public static bool Verify(SignedUpdateManifest envelope, string publicKeyPem)
    {
        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            return key.VerifyData(CanonicalBytes(envelope.Manifest),
                Convert.FromBase64String(envelope.Signature), HashAlgorithmName.SHA256);
        }
        catch (Exception error) when (error is CryptographicException or FormatException)
        {
            return false;
        }
    }
}
