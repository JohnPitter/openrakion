using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using RakionLauncher;
using RakionServer.Common;
using Xunit;

namespace RakionLauncher.Tests;

public sealed class UpdateClientTests
{
    [Fact]
    public async Task AppliesSignedReleaseAndPersistsVersion()
    {
        using var fixture = new UpdateFixture();
        byte[] content = "new-content"u8.ToArray();
        File.WriteAllText(Path.Combine(fixture.Root, "data.bin"), "old");
        SignedUpdateManifest envelope = fixture.Manifest(content, validHash: true);
        var client = new UpdateClient(new HttpClient(
            new FixtureHandler(envelope, content)) { BaseAddress = fixture.BaseUrl });

        int version = await client.ApplyLatestAsync(
            fixture.Root, fixture.Config, progress: null);

        Assert.Equal(259, version);
        Assert.Equal(content, File.ReadAllBytes(Path.Combine(fixture.Root, "data.bin")));
        Assert.Equal("259", File.ReadAllText(Path.Combine(fixture.Root, ".update", "version")));
    }

    [Fact]
    public async Task HashFailureLeavesInstallationUntouched()
    {
        using var fixture = new UpdateFixture();
        byte[] content = "tampered"u8.ToArray();
        File.WriteAllText(Path.Combine(fixture.Root, "data.bin"), "old");
        SignedUpdateManifest envelope = fixture.Manifest(content, validHash: false);
        var client = new UpdateClient(new HttpClient(
            new FixtureHandler(envelope, content)) { BaseAddress = fixture.BaseUrl });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.ApplyLatestAsync(fixture.Root, fixture.Config, progress: null));

        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Root, "data.bin")));
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".update", "version")));
    }

    [Fact]
    public void TransactionRollsBackEarlierFilesWhenLaterTargetFails()
    {
        using var fixture = new UpdateFixture();
        string staging = Path.Combine(fixture.Root, ".update", "staging", "259");
        string backup = Path.Combine(fixture.Root, ".update", "backup", "259");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(fixture.Root, "first.bin"), "old-first");
        File.WriteAllText(Path.Combine(staging, "first.bin"), "new-first");
        Directory.CreateDirectory(Path.Combine(fixture.Root, "blocked"));
        File.WriteAllText(Path.Combine(staging, "blocked"), "new-blocked");
        var files = new[]
        {
            Entry("first.bin", "new-first"u8.ToArray()),
            Entry("blocked", "new-blocked"u8.ToArray())
        };

        Assert.Throws<IOException>(() =>
            UpdateTransaction.Apply(fixture.Root, staging, backup, files));

        Assert.Equal("old-first", File.ReadAllText(Path.Combine(fixture.Root, "first.bin")));
        Assert.True(Directory.Exists(Path.Combine(fixture.Root, "blocked")));
    }

    [Fact]
    public void TransactionRollsBackWhenVersionCommitFails()
    {
        using var fixture = new UpdateFixture();
        string staging = Path.Combine(fixture.Root, ".update", "staging", "259");
        string backup = Path.Combine(fixture.Root, ".update", "backup", "259");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(fixture.Root, "data.bin"), "old");
        File.WriteAllText(Path.Combine(staging, "data.bin"), "new");

        Assert.Throws<InvalidOperationException>(() => UpdateTransaction.Apply(
            fixture.Root, staging, backup,
            [Entry("data.bin", "new"u8.ToArray())],
            () => throw new InvalidOperationException("version write failed")));

        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Root, "data.bin")));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("Bin\\evil.dll")]
    [InlineData("C:/evil.dll")]
    public void PathPolicyRejectsUnsafePaths(string path) =>
        Assert.Throws<ArgumentException>(() => UpdatePathPolicy.NormalizeRelative(path));

    private static UpdateFileEntry Entry(string path, byte[] content) => new(
        path, UpdateOperation.Replace, content.Length,
        Convert.ToHexStringLower(SHA256.HashData(content)), "/file");

    private sealed class FixtureHandler(
        SignedUpdateManifest envelope, byte[] file) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = request.RequestUri!.AbsolutePath.StartsWith("/api/v1/updates/")
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(envelope, options: UpdateManifestCodec.JsonOptions)
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(file)
                };
            return Task.FromResult(response);
        }
    }

    private sealed class UpdateFixture : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "rakion-update-test-" + Guid.NewGuid().ToString("N"));
        public Uri BaseUrl { get; } = new("https://updates.example.test/");
        public LauncherConfig Config { get; }

        public UpdateFixture()
        {
            Directory.CreateDirectory(Root);
            string keyPath = Path.Combine(Root, "public.pem");
            File.WriteAllText(keyPath, _key.ExportSubjectPublicKeyInfoPem());
            Config = new LauncherConfig(true, false, BaseUrl, 11001, 258, keyPath);
        }

        public SignedUpdateManifest Manifest(byte[] content, bool validHash)
        {
            string hash = validHash
                ? Convert.ToHexStringLower(SHA256.HashData(content))
                : new string('0', 64);
            var manifest = new UpdateManifest(1, 11001, 259,
                DateTimeOffset.Parse("2026-07-15T12:00:00Z"),
                [new UpdateFileEntry("data.bin", UpdateOperation.Replace,
                    content.Length, hash, "/files/data.bin")]);
            return new SignedUpdateManifest(manifest,
                UpdateManifestCodec.Sign(manifest, _key.ExportECPrivateKeyPem()));
        }

        public void Dispose()
        {
            _key.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
