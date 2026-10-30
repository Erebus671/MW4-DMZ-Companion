using System.Security.Cryptography;
using DmzCompanion.Core.Loot;
using DmzCompanion.Core.Packaging;

namespace DmzCompanion.Core.Tests.Packaging;

/// <summary>Throwaway signing key and publishing helpers.</summary>
internal sealed class PackagePublisher : IDisposable
{
    public static readonly Uri ManifestUri = new("https://packages.example/loot-manifest.json");

    public ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public string PublicKey => PackageSigning.ExportPublicKey(Key);

    public static Uri PackageUri(long revision) => new($"https://packages.example/loot-package-r{revision}.json");

    public static PackageManifest ManifestFor(LootPackage package, byte[] bytes) => new(
        PackageFormat.CurrentVersion,
        package.Revision,
        package.PublishedUtc,
        PackageUri(package.Revision).ToString(),
        bytes.Length,
        Convert.ToHexStringLower(SHA256.HashData(bytes)));

    public (byte[] Manifest, byte[] Package) Publish(
        LootPackage package, Func<PackageManifest, PackageManifest>? edit = null, ECDsa? signer = null)
    {
        var bytes = PackageJson.Serialize(package);
        var manifest = ManifestFor(package, bytes);
        return (PackageSigning.Sign(edit?.Invoke(manifest) ?? manifest, signer ?? Key), bytes);
    }

    public void Dispose() => Key.Dispose();
}

internal sealed class FakeDownloader : IPackageDownloader
{
    public Dictionary<Uri, byte[]> Files { get; } = [];

    public Dictionary<Uri, Exception> Failures { get; } = [];

    public List<(Uri Uri, long MaxBytes)> Requests { get; } = [];

    public TaskCompletionSource FirstRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Serve((byte[] Manifest, byte[] Package) published, long revision)
    {
        Files[PackagePublisher.ManifestUri] = published.Manifest;
        Files[PackagePublisher.PackageUri(revision)] = published.Package;
    }

    public Task<byte[]> DownloadAsync(Uri uri, long maxBytes, CancellationToken ct)
    {
        lock (Requests)
        {
            Requests.Add((uri, maxBytes));
        }

        FirstRequest.TrySetResult();
        if (Failures.TryGetValue(uri, out var failure))
        {
            return Task.FromException<byte[]>(failure);
        }

        if (!Files.TryGetValue(uri, out var bytes))
        {
            return Task.FromException<byte[]>(new PackageDownloadException(DownloadFailure.HttpStatus, $"{uri} returned HTTP 404."));
        }

        return bytes.Length > maxBytes
            ? Task.FromException<byte[]>(new PackageDownloadException(DownloadFailure.TooLarge, $"{uri} is too large."))
            : Task.FromResult(bytes);
    }
}
