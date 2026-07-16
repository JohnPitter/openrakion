using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.LauncherWeb;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class UpdateReleaseProviderTests
{
    [Fact]
    public async Task BuildsSignedManifestOnlyFromReadyRelease()
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteRelease(259, ready: true);
        fixture.WriteRelease(260, ready: false);
        var provider = new UpdateReleaseProvider(fixture.Config);

        SignedUpdateManifest? envelope = await provider.GetLatestAsync(11001, 258, default);

        Assert.NotNull(envelope);
        Assert.Equal(259, envelope.Manifest.Version);
        Assert.True(UpdateManifestCodec.Verify(envelope, fixture.PublicKey));
        Assert.Equal(new[] { "Bin/game.dll", "obsolete.dat" },
            envelope.Manifest.Files.Select(file => file.Path).ToArray());
        Assert.Equal(UpdateOperation.Delete, envelope.Manifest.Files[1].Operation);
    }

    [Fact]
    public async Task ReturnsNoManifestWhenClientIsCurrent()
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteRelease(259, ready: true);
        var provider = new UpdateReleaseProvider(fixture.Config);

        Assert.Null(await provider.GetLatestAsync(11001, 259, default));
    }

    [Fact]
    public void DownloadResolutionRejectsTraversal()
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteRelease(259, ready: true);
        var provider = new UpdateReleaseProvider(fixture.Config);

        Assert.Throws<ArgumentException>(() =>
            provider.ResolveDownload(11001, 259, "../secret"));
    }

    private sealed class ReleaseFixture : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "rakion-release-test-" + Guid.NewGuid().ToString("N"));
        public string PublicKey => _key.ExportSubjectPublicKeyInfoPem();
        public LauncherWebConfig Config { get; }

        public ReleaseFixture()
        {
            Directory.CreateDirectory(Root);
            Config = new LauncherWebConfig(new Uri("https://updates.example.test"),
                false, true, false, true, 60, null, Root, _key.ExportECPrivateKeyPem());
        }

        public void WriteRelease(int version, bool ready)
        {
            string release = Path.Combine(Root, "11001", version.ToString());
            Directory.CreateDirectory(Path.Combine(release, "Bin"));
            File.WriteAllText(Path.Combine(release, "Bin", "game.dll"), "payload");
            File.WriteAllText(Path.Combine(release, "delete.list"), "obsolete.dat\n");
            if (ready) File.WriteAllText(Path.Combine(release, "_ready"), "");
        }

        public void Dispose()
        {
            _key.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
