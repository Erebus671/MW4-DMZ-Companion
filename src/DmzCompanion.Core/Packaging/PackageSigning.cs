using System.Security.Cryptography;

namespace DmzCompanion.Core.Packaging;

/// <summary>ECDSA P-256 with SHA-256, IEEE P1363 signatures. All in-box .NET crypto.</summary>
public static class PackageSigning
{
    public const int SignatureSize = 64;

    private const string P256Oid = "1.2.840.10045.3.1.7";

    /// <summary>First 8 bytes of the SHA-256 of the public key's SubjectPublicKeyInfo, as lowercase hex.</summary>
    public static string KeyIdOf(ReadOnlySpan<byte> subjectPublicKeyInfo) =>
        Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo)[..8]);

    /// <summary>Public key as base64 SubjectPublicKeyInfo, the form <see cref="PackageTrustedKeys"/> holds.</summary>
    public static string ExportPublicKey(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    /// <exception cref="CryptographicException">The key isn't a P-256 key.</exception>
    public static void RequireP256(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != P256Oid)
        {
            throw new CryptographicException("Package keys must be ECDSA P-256.");
        }
    }

    /// <summary>Signs <paramref name="manifest"/> and returns the published manifest file bytes.</summary>
    /// <exception cref="CryptographicException">The key can't sign.</exception>
    public static byte[] Sign(PackageManifest manifest, ECDsa privateKey)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        RequireP256(privateKey);

        var payload = PackageJson.Serialize(manifest);
        var signature = privateKey.SignData(
            WithContext(payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var keyId = KeyIdOf(privateKey.ExportSubjectPublicKeyInfo());
        return PackageJson.Serialize(new SignedManifest(keyId, Convert.ToBase64String(payload), Convert.ToBase64String(signature)));
    }

    internal static bool Verify(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        if (signature.Length != SignatureSize)
        {
            return false;
        }

        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
        return key.VerifyData(
            WithContext(payload), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static byte[] WithContext(ReadOnlySpan<byte> payload)
    {
        var context = PackageFormat.SignatureContext;
        var data = new byte[context.Length + payload.Length];
        context.CopyTo(data);
        payload.CopyTo(data.AsSpan(context.Length));
        return data;
    }
}
