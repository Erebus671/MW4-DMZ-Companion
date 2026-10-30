using System.Security.Cryptography;
using System.Text.Json;
using DmzCompanion.Core.Loot;
using DmzCompanion.Core.Packaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DmzCompanion.PackageSigner;

internal static class Program
{
    private const string Usage = """
        Usage:
          keygen <private-key.pem>
          sign <package.json> <private-key.pem> <package-url> <manifest-out.json>
          verify <manifest.json> <package.json> [public-key-base64]
        """;

    private static int Main(string[] args)
    {
        try
        {
            return args switch
            {
                ["keygen", var key] => KeyGen(key),
                ["sign", var package, var key, var url, var output] => Sign(package, key, url, output),
                ["verify", var manifest, var package] => Verify(manifest, package, PackageTrustedKeys.All),
                ["verify", var manifest, var package, var publicKey] => Verify(manifest, package, [publicKey]),
                _ => Fail(Usage),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException
            or JsonException or ArgumentException)
        {
            return Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int KeyGen(string path)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var writer = new StreamWriter(path, options))
        {
            writer.Write(key.ExportPkcs8PrivateKeyPem());
        }

        var publicKey = key.ExportSubjectPublicKeyInfo();
        Console.WriteLine($"Private key: {Path.GetFullPath(path)} (keep it out of the repo)");
        Console.WriteLine($"Key id:      {PackageSigning.KeyIdOf(publicKey)}");
        Console.WriteLine($"Public key:  {Convert.ToBase64String(publicKey)}");
        return 0;
    }

    private static int Sign(string packagePath, string keyPath, string packageUrl, string outputPath)
    {
        var bytes = File.ReadAllBytes(packagePath);
        var package = PackageJson.DeserializePackage(bytes);
        var errors = LootPackageValidator.Validate(package);
        if (errors.Count > 0)
        {
            return Fail("Package is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        var manifest = new PackageManifest(
            PackageFormat.CurrentVersion,
            package.Revision,
            package.PublishedUtc,
            packageUrl,
            bytes.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        if (manifest.FindProblem(PackageFormat.MaxPackageBytes) is { } problem)
        {
            return Fail($"Manifest is invalid: {problem}");
        }

        using var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(keyPath));
        File.WriteAllBytes(outputPath, PackageSigning.Sign(manifest, key));
        Console.WriteLine($"Signed revision {manifest.Revision} with key {PackageSigning.KeyIdOf(key.ExportSubjectPublicKeyInfo())}.");
        return 0;
    }

    private static int Verify(string manifestPath, string packagePath, IReadOnlyList<string> publicKeys)
    {
        var keys = PackageKeyRing.Create(publicKeys, NullLogger.Instance);
        if (keys.Count == 0)
        {
            return Fail("No usable public key.");
        }

        var manifest = PackageVerifier.OpenManifest(File.ReadAllBytes(manifestPath), keys);
        if (manifest.Value is null)
        {
            return Fail($"{manifest.Status}: {manifest.Problem}");
        }

        var package = PackageVerifier.OpenPackage(manifest.Value, File.ReadAllBytes(packagePath));
        if (package.Value is null)
        {
            return Fail($"{package.Status}: {package.Problem}");
        }

        var errors = LootPackageValidator.Validate(package.Value);
        if (errors.Count > 0)
        {
            return Fail("Package is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        Console.WriteLine($"OK: revision {manifest.Value.Revision}, {manifest.Value.PackageSize} bytes.");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
