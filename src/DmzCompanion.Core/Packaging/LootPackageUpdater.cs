using DmzCompanion.Core.Models;
using DmzCompanion.Core.Storage;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Packaging;

public sealed record PackageUpdateOptions
{
    public Uri ManifestUrl { get; init; } = new(AppConstants.LootManifestUrl);

    public IReadOnlyList<string> TrustedKeys { get; init; } = PackageTrustedKeys.All;

    public long MaxPackageBytes { get; init; } = PackageFormat.MaxPackageBytes;

    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromHours(6);

    /// <summary>Delay after a failure that may clear up by itself.</summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromMinutes(30);
}

public enum PackageUpdateStatus
{
    Installed,
    UpToDate,
    NotConfigured,
    DownloadFailed,
    TooLarge,
    BadSignature,
    BadManifest,
    Downgrade,
    AppUpdateRequired,
    Tampered,
    InvalidData,
    StorageFailed,
    Failed,
}

public sealed record PackageUpdateResult(PackageUpdateStatus Status, long? InstalledRevision, string? Problem = null)
{
    public bool IsRetryable => Status is PackageUpdateStatus.DownloadFailed or PackageUpdateStatus.StorageFailed
        or PackageUpdateStatus.Failed;
}

/// <summary>Checks for a newer signed package and installs it. Any failure keeps the installed package.</summary>
public sealed class LootPackageUpdater
{
    private readonly LootPackageStore _store;
    private readonly IPackageDownloader _downloader;
    private readonly PackageUpdateOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<LootPackageUpdater> _logger;
    private readonly PackageKeyRing _keys;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LootPackageUpdater(
        LootPackageStore store,
        IPackageDownloader downloader,
        PackageUpdateOptions options,
        TimeProvider time,
        ILogger<LootPackageUpdater> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxPackageBytes, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxPackageBytes, PackageFormat.MaxPackageBytes, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.CheckInterval, TimeSpan.Zero, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.RetryInterval, TimeSpan.Zero, nameof(options));

        _store = store;
        _downloader = downloader;
        _options = options;
        _time = time;
        _logger = logger;
        _keys = PackageKeyRing.Create(options.TrustedKeys, logger);
    }

    /// <summary>One update check. Never throws except for cancellation.</summary>
    public async Task<PackageUpdateResult> CheckAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await CheckCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Checks now, then on the check interval, or the retry interval after a transient failure, until canceled.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (true)
        {
            var result = await CheckAsync(ct).ConfigureAwait(false);
            var delay = result.IsRetryable ? _options.RetryInterval : _options.CheckInterval;
            await Task.Delay(delay, _time, ct).ConfigureAwait(false);
        }
    }

    private async Task<PackageUpdateResult> CheckCoreAsync(CancellationToken ct)
    {
        long? installed = null;
        try
        {
            if (_keys.Count == 0)
            {
                return Log(new(PackageUpdateStatus.NotConfigured, null, "no trusted signing keys are configured"));
            }

            installed = (await _store.GetInfoAsync(ct).ConfigureAwait(false))?.Revision;

            var manifestBytes = await _downloader
                .DownloadAsync(_options.ManifestUrl, PackageFormat.MaxManifestBytes, ct)
                .ConfigureAwait(false);
            var opened = PackageVerifier.OpenManifest(manifestBytes, _keys, _options.MaxPackageBytes);
            if (opened.Value is not { } manifest)
            {
                return Log(new(opened.Status, installed, opened.Problem), opened.Error);
            }

            if (manifest.FormatVersion > PackageFormat.CurrentVersion)
            {
                return Log(new(
                    PackageUpdateStatus.AppUpdateRequired,
                    installed,
                    $"manifest format {manifest.FormatVersion} is newer than supported format {PackageFormat.CurrentVersion}"));
            }

            if (installed is { } current && manifest.Revision <= current)
            {
                return manifest.Revision == current
                    ? Log(new(PackageUpdateStatus.UpToDate, installed))
                    : Log(new(
                        PackageUpdateStatus.Downgrade,
                        installed,
                        $"manifest revision {manifest.Revision} is older than installed revision {current}"));
            }

            var packageBytes = await _downloader
                .DownloadAsync(new Uri(manifest.PackageUrl), manifest.PackageSize, ct)
                .ConfigureAwait(false);
            var package = PackageVerifier.OpenPackage(manifest, packageBytes);
            if (package.Value is null)
            {
                return Log(new(package.Status, installed, package.Problem), package.Error);
            }

            if (!await _store.TryInstallNewerAsync(package.Value, ct).ConfigureAwait(false))
            {
                return Log(new(
                    PackageUpdateStatus.Downgrade,
                    installed,
                    $"revision {manifest.Revision} lost to a newer install that finished first"));
            }

            return Log(new(PackageUpdateStatus.Installed, manifest.Revision));
        }
        catch (PackageDownloadException ex)
        {
            var status = ex.Failure == DownloadFailure.TooLarge ? PackageUpdateStatus.TooLarge : PackageUpdateStatus.DownloadFailed;
            return Log(new(status, installed, ex.Message), ex);
        }
        catch (InvalidLootPackageException ex)
        {
            // Store already logged the validation errors.
            return Log(new(PackageUpdateStatus.InvalidData, installed, ex.Message), ex);
        }
        catch (LootCacheException ex)
        {
            // Store already logged the cache failure.
            return Log(new(PackageUpdateStatus.StorageFailed, installed, ex.Message), ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Package update check failed unexpectedly; the installed package is kept.");
            return new(PackageUpdateStatus.Failed, installed, ex.Message);
        }
    }

    /// <param name="cause">Expected failure detail, logged at Debug only.</param>
    private PackageUpdateResult Log(PackageUpdateResult result, Exception? cause = null)
    {
        if (cause is not null)
        {
            _logger.LogDebug(cause, "Package update {Status} detail.", result.Status);
        }

        switch (result.Status)
        {
            case PackageUpdateStatus.Installed:
                _logger.LogInformation("Installed loot package revision {Revision}.", result.InstalledRevision);
                break;
            case PackageUpdateStatus.UpToDate:
                _logger.LogDebug("Loot package revision {Revision} is current.", result.InstalledRevision);
                break;
            case PackageUpdateStatus.DownloadFailed:
            case PackageUpdateStatus.StorageFailed:
                _logger.LogInformation(
                    "Package update skipped ({Status}): {Problem}. Keeping revision {Revision}.",
                    result.Status, result.Problem, result.InstalledRevision);
                break;
            default:
                _logger.LogWarning(
                    "Package update rejected ({Status}): {Problem}. Keeping revision {Revision}.",
                    result.Status, result.Problem, result.InstalledRevision);
                break;
        }

        return result;
    }
}
