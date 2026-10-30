using System.Security.Cryptography;
using System.Text;
using DmzCompanion.Core.Loot;
using DmzCompanion.Core.Packaging;
using DmzCompanion.Core.Storage;
using DmzCompanion.Core.Tests.Storage;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Tests.Packaging;

public sealed class LootPackageUpdaterTests : CacheTestBase, IAsyncLifetime
{
    private readonly PackagePublisher _publisher = new();
    private readonly FakeDownloader _downloader = new();
    private readonly CaptureLogger<LootPackageStore> _storeLogger = new();
    private readonly CaptureLogger<LootPackageUpdater> _logger = new();
    private CacheDatabase? _db;

    private LootPackageStore Store => new(_db!, _storeLogger);

    public async ValueTask InitializeAsync() => _db = await OpenAsync(Ct);

    public ValueTask DisposeAsync()
    {
        _db?.Dispose();
        _publisher.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ValidPackage_Installs()
    {
        var package = SamplePackage.Create(1);
        _downloader.Serve(_publisher.Publish(package), 1);

        var result = await NewUpdater().CheckAsync(Ct);

        Assert.Equal(new PackageUpdateResult(PackageUpdateStatus.Installed, 1), result);
        Assert.Equivalent(package, await Store.LoadAsync(Ct), strict: true);
        Assert.Contains(_logger.Records, r => r.Level == LogLevel.Information);
        Assert.Equal(
            [(PackagePublisher.ManifestUri, (long)PackageFormat.MaxManifestBytes), (PackagePublisher.PackageUri(1), (long)_downloader.Files[PackagePublisher.PackageUri(1)].Length)],
            _downloader.Requests);
    }

    [Fact]
    public async Task NewerPackage_ReplacesInstalled()
    {
        await Store.ReplaceAsync(SamplePackage.Create(1), Ct);
        _downloader.Serve(_publisher.Publish(SamplePackage.Create(2)), 2);

        var result = await NewUpdater().CheckAsync(Ct);

        Assert.Equal(PackageUpdateStatus.Installed, result.Status);
        Assert.Equal(2, await InstalledRevisionAsync());
    }

    [Fact]
    public async Task SameRevision_IsUpToDateWithoutDownloading()
    {
        await Store.ReplaceAsync(SamplePackage.Create(2), Ct);
        _downloader.Serve(_publisher.Publish(SamplePackage.Create(2)), 2);

        var result = await NewUpdater().CheckAsync(Ct);

        Assert.Equal(new PackageUpdateResult(PackageUpdateStatus.UpToDate, 2), result);
        Assert.Single(_downloader.Requests);
        Assert.DoesNotContain(_logger.Records, r => r.Level >= LogLevel.Information);
    }

    [Fact]
    public async Task UntrustedKey_IsRejected()
    {
        await Store.ReplaceAsync(SamplePackage.Create(1), Ct);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _downloader.Serve(_publisher.Publish(SamplePackage.Create(2), signer: other), 2);

        await AssertRejectedAsync(PackageUpdateStatus.BadSignature, kept: 1);
        Assert.Single(_downloader.Requests);
    }

    [Fact]
    public async Task ForgedSignatureUnderTrustedKeyId_IsRejected()
    {
        await Store.ReplaceAsync(SamplePackage.Create(1), Ct);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (forged, package) = _publisher.Publish(SamplePackage.Create(2), signer: other);
        var envelope = PackageJson.DeserializeSignedManifest(forged) with
        {
            KeyId = PackageSigning.KeyIdOf(_publisher.Key.ExportSubjectPublicKeyInfo()),
        };
        _downloader.Serve((PackageJson.Serialize(envelope), package), 2);

        await AssertRejectedAsync(PackageUpdateStatus.BadSignature, kept: 1);
    }

    [Fact]
    public async Task TamperedManifestPayload_IsRejected()
    {
        await Store.ReplaceAsync(SamplePackage.Create(1), Ct);
        var (signed, package) = _publisher.Publish(SamplePackage.Create(2));
        var envelope = PackageJson.DeserializeSignedManifest(signed);
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(envelope.Payload)).Replace("\"revision\":2", "\"revision\":9", StringComparison.Ordinal);
        var tampered = envelope with { Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)) };
        _downloader.Serve((PackageJson.Serialize(tampered), package), 2);

        await AssertRejectedAsync(PackageUpdateStatus.BadSignature, kept: 1);
        Assert.Single(_downloader.Requests);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"keyId\":\"x\"}")]
    [InlineData("{\"keyId\":\"a\",\"keyId\":\"b\",\"payload\":\"\",\"signature\":\"\"}")]
    public async Task MalformedManifestFile_IsRejected(string json)
    {
        await Store.ReplaceAsync(SamplePackage.Create(1), Ct);
        _downloader.Files[PackagePublisher.ManifestUri] = Encoding.UTF8.GetBytes(json);

        await AssertRejectedAsync(PackageUpdateStatus.BadManifest, kept: 1);
    }

    [Fact]
    public async Task TamperedPayload_IsRejected()
    {
        await Store.ReplaceAsync(SamplePackage.Create(1), Ct);
        var (manifest, package) = _publisher.Publish(SamplePackage.Create(2));
        var tampered = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(package).Replace("Copper Wires", "Copper Wirez", StringComparison.Ordinal));
        Assert.Equal(package.Length, tampered.Length);
        _downloader.Serve((manifest, tampered), 2);

        await AssertRejectedAsync(PackageUpdateStatus.Tampered, kept: 1);
        Assert.Null(_storeLogger.Records.FirstOrDefault(r => r.Level >= LogLevel.Warning));
    }

    [Fact]
    public async Task TruncatedPayload_IsRejected()
    {
        await Store.ReplaceAsync(SamplePackage.Create(1), Ct);
        var (manifest, package) = _publisher.Publish(SamplePackage.Create(2));
        _downloader.Serve((manifest, package[..^1]), 2);

        await AssertRejectedAsync(PackageUpdateStatus.Tampered, kept: 1);
    }

    [Fact]
    public async Task PackageRevisionMismatch_IsRejected()
    {
        await Store.ReplaceAsync(SamplePackage.Create(1), Ct);
        var bytes = PackageJson.Serialize(SamplePackage.Create(3));
        var manifest = PackagePublisher.ManifestFor(SamplePackage.Create(2), bytes);
        _downloader.Serve((PackageSigning.Sign(manifest, _publisher.Key), bytes), 2);

        await AssertRejectedAsync(PackageUpdateStatus.Tampered, kept: 1);
    }

    [Fact]
    public async Task Downgrade_IsRejected()
    {
        await Store.ReplaceAsync(SamplePackage.Create(3), Ct);
        _downloader.Serve(_publisher.Publish(SamplePackage.Create(2)), 2);

        await AssertRejectedAsync(PackageUpdateStatus.Downgrade, kept: 3);
        Assert.Single(_downloader.Requests);
    }

    [Fact]
    public async Task OversizedDownload_IsRejected()
    {
        await Store.ReplaceAsync(SamplePackage.Create(1), Ct);
        var (manifest, package) = _publisher.Publish(SamplePackage.Create(2));
        _downloader.Serve((manifest, [.. package, .. "          "u8]), 2);

        await AssertRejectedAsync(PackageUpdateStatus.TooLarge, kept: 1);
        Assert.Equal(package.Length, _downloader.Requests[^1].MaxBytes);
    }

    [Fact]
    public async Task ManifestOverSizeLimit_IsRejectedBeforeDownload()
    {
        await Store.ReplaceAsync(SamplePackage.Create(1), Ct);
        _downloader.Serve(_publisher.Publish(SamplePackage.Create(2)), 2);

        var result = await NewUpdater(new PackageUpdateOptions
        {
            ManifestUrl = PackagePublisher.ManifestUri,
            TrustedKeys = [_publisher.PublicKey],
            MaxPackageBytes = 100,
        }).CheckAsync(Ct);

        Assert.Equal(PackageUpdateStatus.BadManifest, result.Status);
        Assert.Single(_downloader.Requests);
        Assert.Equal(1, await InstalledRevisionAsync());
    }

    [Fact]
    public async Task InsecurePackageUrl_IsRejected()
    {
        await Store.ReplaceAsync(SamplePackage.Create(1), Ct);
        _downloader.Serve(_publisher.Publish(SamplePackage.Create(2), m => m with { PackageUrl = "http://packages.example/p.json" }), 2);

        await AssertRejectedAsync(PackageUpdateStatus.BadManifest, kept: 1);
    }

    [Fact]
    public async Task NewerFormat_RequiresAppUpdate()
    {
        await Store.ReplaceAsync(SamplePackage.Create(1), Ct);
        _downloader.Serve(_publisher.Publish(SamplePackage.Create(2), m => m with { FormatVersion = PackageFormat.CurrentVersion + 1 }), 2);

        await AssertRejectedAsync(PackageUpdateStatus.AppUpdateRequired, kept: 1);
    }

    [Fact]
    public async Task InvalidContent_IsRejectedAndOldPackageKept()
    {
        await Store.ReplaceAsync(SamplePackage.Create(1), Ct);
        var bad = SamplePackage.Create(2) with { Items = [new LootItem("wires", "W", "missing", Rarity.Common, 1, 1)] };
        _downloader.Serve(_publisher.Publish(bad), 2);

        var result = await NewUpdater().CheckAsync(Ct);

        Assert.Equal(PackageUpdateStatus.InvalidData, result.Status);
        Assert.Equal(1, await InstalledRevisionAsync());
    }

    [Fact]
    public async Task SignedButMalformedPackage_IsRejected()
    {
        await Store.ReplaceAsync(SamplePackage.Create(1), Ct);
        var bytes = "{\"revision\":2}"u8.ToArray();
        var manifest = PackagePublisher.ManifestFor(SamplePackage.Create(2), bytes);
        _downloader.Serve((PackageSigning.Sign(manifest, _publisher.Key), bytes), 2);

        await AssertRejectedAsync(PackageUpdateStatus.InvalidData, kept: 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NetworkFailure_KeepsOldPackage(bool duringManifest)
    {
        await Store.ReplaceAsync(SamplePackage.Create(1), Ct);
        _downloader.Serve(_publisher.Publish(SamplePackage.Create(2)), 2);
        var failing = duringManifest ? PackagePublisher.ManifestUri : PackagePublisher.PackageUri(2);
        _downloader.Failures[failing] = new PackageDownloadException(
            DownloadFailure.Network, "offline", new HttpRequestException("offline"));

        var result = await NewUpdater().CheckAsync(Ct);

        Assert.Equal(PackageUpdateStatus.DownloadFailed, result.Status);
        Assert.True(result.IsRetryable);
        Assert.Equal(1, result.InstalledRevision);
        Assert.Equivalent(SamplePackage.Create(1), await Store.LoadAsync(Ct), strict: true);
        Assert.DoesNotContain(_logger.Records, r => r.Level >= LogLevel.Warning);
        Assert.All(_logger.Records.Where(r => r.Level > LogLevel.Debug), r => Assert.Null(r.Exception));
    }

    [Fact]
    public async Task UnexpectedFailure_IsLoggedAndOldPackageKept()
    {
        await Store.ReplaceAsync(SamplePackage.Create(1), Ct);
        _downloader.Failures[PackagePublisher.ManifestUri] = new InvalidOperationException("boom");

        var result = await NewUpdater().CheckAsync(Ct);

        Assert.Equal(PackageUpdateStatus.Failed, result.Status);
        Assert.Equal(1, await InstalledRevisionAsync());
        Assert.Contains(_logger.Records, r => r.Level == LogLevel.Error && r.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task NoTrustedKeys_SkipsCheck()
    {
        var result = await NewUpdater(new PackageUpdateOptions { ManifestUrl = PackagePublisher.ManifestUri, TrustedKeys = [] })
            .CheckAsync(Ct);

        Assert.Equal(PackageUpdateStatus.NotConfigured, result.Status);
        Assert.Empty(_downloader.Requests);
    }

    [Fact]
    public async Task UnusableTrustedKey_IsLoggedAndSkipped()
    {
        _downloader.Serve(_publisher.Publish(SamplePackage.Create(1)), 1);
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        var result = await NewUpdater(new PackageUpdateOptions
        {
            ManifestUrl = PackagePublisher.ManifestUri,
            TrustedKeys = ["not base64!", PackageSigning.ExportPublicKey(p384), _publisher.PublicKey],
        }).CheckAsync(Ct);

        Assert.Equal(PackageUpdateStatus.Installed, result.Status);
        Assert.Equal(2, _logger.Records.Count(r => r.Level == LogLevel.Error));
    }

    [Fact]
    public async Task Run_ChecksImmediatelyAndStopsOnCancel()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = NewUpdater().RunAsync(cts.Token);

        await _downloader.FirstRequest.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task Check_PropagatesCancellation()
    {
        _downloader.Failures[PackagePublisher.ManifestUri] = new OperationCanceledException();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NewUpdater().CheckAsync(cts.Token));
    }

    private LootPackageUpdater NewUpdater(PackageUpdateOptions? options = null) => new(
        Store,
        _downloader,
        options ?? new PackageUpdateOptions { ManifestUrl = PackagePublisher.ManifestUri, TrustedKeys = [_publisher.PublicKey] },
        Time,
        _logger);

    private async Task<long?> InstalledRevisionAsync() => (await Store.GetInfoAsync(Ct))?.Revision;

    private async Task AssertRejectedAsync(PackageUpdateStatus expected, long kept)
    {
        var result = await NewUpdater().CheckAsync(Ct);

        Assert.Equal(expected, result.Status);
        Assert.NotNull(result.Problem);
        Assert.False(result.IsRetryable);
        Assert.Equal(kept, result.InstalledRevision);
        Assert.Equal(kept, await InstalledRevisionAsync());
        Assert.Contains(_logger.Records, r => r.Level == LogLevel.Warning);
    }
}
