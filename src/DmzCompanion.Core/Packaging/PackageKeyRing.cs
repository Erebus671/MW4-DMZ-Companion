using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Packaging;

/// <summary>Public keys trusted to sign data packages, as base64 SubjectPublicKeyInfo. Keep a retired key until no supported build needs it.</summary>
public static class PackageTrustedKeys
{
    // Empty until the production key exists (tools/DmzCompanion.PackageSigner keygen). Updates stay off until then.
    public static IReadOnlyList<string> All { get; } = [];
}

/// <summary>Trusted public keys by key id.</summary>
public sealed class PackageKeyRing
{
    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.Ordinal);

    private PackageKeyRing()
    {
    }

    public int Count => _keys.Count;

    /// <summary>Unusable keys are logged and skipped so a bad entry can't take the app down.</summary>
    public static PackageKeyRing Create(IEnumerable<string> subjectPublicKeyInfos, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(subjectPublicKeyInfos);
        ArgumentNullException.ThrowIfNull(logger);

        var ring = new PackageKeyRing();
        var index = 0;
        foreach (var encoded in subjectPublicKeyInfos)
        {
            try
            {
                var spki = Convert.FromBase64String(encoded);
                using (var key = ECDsa.Create())
                {
                    key.ImportSubjectPublicKeyInfo(spki, out _);
                    PackageSigning.RequireP256(key);
                }

                ring._keys.TryAdd(PackageSigning.KeyIdOf(spki), spki);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentNullException)
            {
                logger.LogError(ex, "Trusted package key {Index} is unusable; skipped.", index);
            }

            index++;
        }

        return ring;
    }

    public bool TryGet(string keyId, [NotNullWhen(true)] out byte[]? subjectPublicKeyInfo) =>
        _keys.TryGetValue(keyId, out subjectPublicKeyInfo);
}
