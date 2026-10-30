using DmzCompanion.Core.Loot;

namespace DmzCompanion.Core.Packaging;

public static class PackageFormat
{
    /// <summary>Highest manifest format this build understands. Bump only for breaking changes.</summary>
    public const int CurrentVersion = 1;

    public const int MaxManifestBytes = 64 * 1024;

    public const long MaxPackageBytes = 32 * 1024 * 1024;

    public const int Sha256HexLength = 64;

    /// <summary>Domain separation prefix signed ahead of the manifest payload.</summary>
    internal static ReadOnlySpan<byte> SignatureContext => "DmzCompanion.LootManifest.v1\n"u8;
}

/// <summary>Describes the latest data package. Published signed, next to the package it pins by size and hash.</summary>
public sealed record PackageManifest(
    int FormatVersion,
    long Revision,
    DateTimeOffset PublishedUtc,
    string PackageUrl,
    long PackageSize,
    string PackageSha256)
{
    /// <summary>First problem found, or null.</summary>
    public string? FindProblem(long maxPackageBytes) =>
        FormatVersion < 1 ? "format version must be at least 1"
        : Revision < 1 ? "revision must be at least 1"
        : !LootPackageValidator.IsHttpsUrl(PackageUrl) ? "package URL must be an absolute https URL"
        : PackageSize < 1 || PackageSize > maxPackageBytes ? "package size is out of range"
        : !IsSha256Hex(PackageSha256) ? "package hash must be 64 hex characters"
        : null;

    private static bool IsSha256Hex(string? value) =>
        value is { Length: PackageFormat.Sha256HexLength } && value.All(char.IsAsciiHexDigit);
}

/// <summary>Manifest file as published: base64 manifest JSON, its ECDSA P-256 signature, and the signing key id.</summary>
public sealed record SignedManifest(string KeyId, string Payload, string Signature);
