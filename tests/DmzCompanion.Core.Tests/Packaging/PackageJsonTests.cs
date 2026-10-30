using System.Security.Cryptography;
using System.Text.Json;
using DmzCompanion.Core.Loot;
using DmzCompanion.Core.Packaging;
using DmzCompanion.Core.Tests.Storage;

namespace DmzCompanion.Core.Tests.Packaging;

public sealed class PackageJsonTests
{
    [Fact]
    public void Package_RoundTrips()
    {
        var package = SamplePackage.Create(4);

        var loaded = PackageJson.DeserializePackage(PackageJson.Serialize(package));

        Assert.Equivalent(package, loaded, strict: true);
    }

    [Fact]
    public void ComputedProperties_AreNotSerialized()
    {
        var json = System.Text.Encoding.UTF8.GetString(PackageJson.Serialize(SamplePackage.Create()));

        Assert.DoesNotContain("isNormalized", json, StringComparison.Ordinal);
        Assert.DoesNotContain("isValid", json, StringComparison.Ordinal);
        Assert.Contains("\"unverified\":false", json, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownMembers_AreSkipped()
    {
        var package = PackageJson.DeserializePackage("""{"revision":3,"publishedUtc":"2026-10-01T12:00:00Z","reviewedBy":"x"}"""u8);

        Assert.Equal(3, package.Revision);
        Assert.Empty(package.Maps);
        Assert.Empty(package.Recipes);
        Assert.Empty(LootPackageValidator.Validate(package));
    }

    [Fact]
    public void OmittedUnverifiedFlag_DefaultsToVerified()
    {
        var package = PackageJson.DeserializePackage(
            """{"revision":1,"publishedUtc":"2026-10-01T12:00:00Z","recipes":[{"id":"a","categoryId":"c","name":"n","tier":0,"description":"d","cashCost":0,"ingredients":[],"components":[]}]}"""u8);

        Assert.False(Assert.Single(package.Recipes).Unverified);
    }

    [Theory]
    [InlineData("""{"publishedUtc":"2026-10-01T12:00:00Z"}""")]
    [InlineData("""{"revision":1,"revision":2,"publishedUtc":"2026-10-01T12:00:00Z"}""")]
    [InlineData("""{"revision":1,"publishedUtc":"2026-10-01T12:00:00Z","maps":null}""")]
    [InlineData("""{"revision":1,"publishedUtc":"2026-10-01T12:00:00Z","maps":[null]}""")]
    [InlineData("""{"revision":1,"publishedUtc":"2026-10-01T12:00:00Z","recipes":[{"id":"a","categoryId":"c","name":"n","tier":0,"description":"d","cashCost":0,"ingredients":[null],"components":[]}]}""")]
    [InlineData("""{"revision":1,"publishedUtc":"2026-10-01T12:00:00Z","recipes":[{"id":"a","categoryId":"c","name":"n","tier":0,"description":"d","cashCost":0,"components":[]}]}""")]
    [InlineData("null")]
    public void InvalidPackage_Throws(string json)
    {
        Assert.Throws<JsonException>(() => PackageJson.DeserializePackage(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Sign_ProducesVerifiableManifest()
    {
        using var publisher = new PackagePublisher();
        var (signed, package) = publisher.Publish(SamplePackage.Create(2));
        var ring = PackageKeyRing.Create([publisher.PublicKey], new CaptureLogger<PackageJsonTests>());

        var manifest = PackageVerifier.OpenManifest(signed, ring);
        var opened = PackageVerifier.OpenPackage(manifest.Value!, package);

        Assert.True(manifest.IsValid);
        Assert.True(opened.IsValid);
        Assert.Equal(2, opened.Value!.Revision);
    }

    [Fact]
    public void Sign_RejectsNonP256Key()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var manifest = new PackageManifest(1, 1, SamplePackage.Published, "https://packages.example/p.json", 1, new string('a', 64));

        Assert.Throws<CryptographicException>(() => PackageSigning.Sign(manifest, p384));
    }

    [Theory]
    [InlineData(0, 1, "https://p.example/p.json", 1, 64)]
    [InlineData(1, 0, "https://p.example/p.json", 1, 64)]
    [InlineData(1, 1, "ftp://p.example/p.json", 1, 64)]
    [InlineData(1, 1, "https://p.example/p.json", 0, 64)]
    [InlineData(1, 1, "https://p.example/p.json", 1, 63)]
    public void ManifestProblems_AreFound(int format, long revision, string url, long size, int hashLength)
    {
        var manifest = new PackageManifest(format, revision, SamplePackage.Published, url, size, new string('a', hashLength));

        Assert.NotNull(manifest.FindProblem(PackageFormat.MaxPackageBytes));
    }
}
