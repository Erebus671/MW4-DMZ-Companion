using System.Security.Cryptography;
using System.Text.Json;
using DmzCompanion.Core.Loot;

namespace DmzCompanion.Core.Packaging;

public sealed record PackageVerification<T>(T? Value, PackageUpdateStatus Status, string? Problem, Exception? Error = null)
    where T : class
{
    public bool IsValid => Value is not null;
}

/// <summary>Signature, size and hash checks. Nothing is parsed into the model before its bytes are verified.</summary>
public static class PackageVerifier
{
    public static PackageVerification<PackageManifest> OpenManifest(
        ReadOnlySpan<byte> signedManifest,
        PackageKeyRing keys,
        long maxPackageBytes = PackageFormat.MaxPackageBytes)
    {
        ArgumentNullException.ThrowIfNull(keys);

        SignedManifest envelope;
        try
        {
            envelope = PackageJson.DeserializeSignedManifest(signedManifest);
        }
        catch (JsonException ex)
        {
            return Fail<PackageManifest>(PackageUpdateStatus.BadManifest, $"manifest file is malformed: {ex.Message}", ex);
        }

        if (!keys.TryGet(envelope.KeyId, out var publicKey))
        {
            return Fail<PackageManifest>(PackageUpdateStatus.BadSignature, $"signing key '{envelope.KeyId}' isn't trusted");
        }

        byte[] payload;
        bool signed;
        try
        {
            payload = Convert.FromBase64String(envelope.Payload);
            signed = PackageSigning.Verify(payload, Convert.FromBase64String(envelope.Signature), publicKey);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return Fail<PackageManifest>(PackageUpdateStatus.BadSignature, $"signature can't be checked: {ex.Message}", ex);
        }

        if (!signed)
        {
            return Fail<PackageManifest>(PackageUpdateStatus.BadSignature, "signature doesn't match");
        }

        PackageManifest manifest;
        try
        {
            manifest = PackageJson.DeserializeManifest(payload);
        }
        catch (JsonException ex)
        {
            return Fail<PackageManifest>(PackageUpdateStatus.BadManifest, $"signed manifest is malformed: {ex.Message}", ex);
        }

        return manifest.FindProblem(maxPackageBytes) is { } problem
            ? Fail<PackageManifest>(PackageUpdateStatus.BadManifest, problem)
            : new(manifest, PackageUpdateStatus.Installed, null);
    }

    /// <summary>Checks the package bytes against the verified manifest, then parses them. Doesn't run the content validator.</summary>
    public static PackageVerification<LootPackage> OpenPackage(PackageManifest manifest, ReadOnlySpan<byte> package)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (package.Length != manifest.PackageSize)
        {
            return Fail<LootPackage>(
                PackageUpdateStatus.Tampered, $"package is {package.Length} bytes, manifest says {manifest.PackageSize}");
        }

        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(package), Convert.FromHexString(manifest.PackageSha256)))
        {
            return Fail<LootPackage>(PackageUpdateStatus.Tampered, "package hash doesn't match the manifest");
        }

        LootPackage parsed;
        try
        {
            parsed = PackageJson.DeserializePackage(package);
        }
        catch (JsonException ex)
        {
            return Fail<LootPackage>(PackageUpdateStatus.InvalidData, $"package is malformed: {ex.Message}", ex);
        }

        return parsed.Revision != manifest.Revision
            ? Fail<LootPackage>(
                PackageUpdateStatus.Tampered, $"package revision {parsed.Revision} doesn't match manifest revision {manifest.Revision}")
            : new(parsed, PackageUpdateStatus.Installed, null);
    }

    private static PackageVerification<T> Fail<T>(PackageUpdateStatus status, string problem, Exception? error = null)
        where T : class => new(null, status, problem, error);
}
