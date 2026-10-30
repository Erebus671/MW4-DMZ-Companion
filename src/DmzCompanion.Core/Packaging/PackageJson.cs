using System.Text.Json;
using System.Text.Json.Serialization;
using DmzCompanion.Core.Loot;

namespace DmzCompanion.Core.Packaging;

/// <summary>Strict, reflection-free JSON for packages and manifests. Unknown members are skipped so new fields stay additive.</summary>
public static class PackageJson
{
    /// <exception cref="JsonException">The JSON is malformed or doesn't match the format.</exception>
    public static LootPackage DeserializePackage(ReadOnlySpan<byte> utf8) =>
        Complete(JsonSerializer.Deserialize(utf8, PackageJsonContext.Default.LootPackage)
            ?? throw new JsonException("Package is null."));

    /// <exception cref="JsonException">The JSON is malformed or doesn't match the format.</exception>
    public static PackageManifest DeserializeManifest(ReadOnlySpan<byte> utf8) =>
        JsonSerializer.Deserialize(utf8, PackageJsonContext.Default.PackageManifest)
        ?? throw new JsonException("Manifest is null.");

    /// <exception cref="JsonException">The JSON is malformed or doesn't match the format.</exception>
    public static SignedManifest DeserializeSignedManifest(ReadOnlySpan<byte> utf8) =>
        JsonSerializer.Deserialize(utf8, PackageJsonContext.Default.SignedManifest)
        ?? throw new JsonException("Signed manifest is null.");

    public static byte[] Serialize(LootPackage package) =>
        JsonSerializer.SerializeToUtf8Bytes(package, PackageJsonContext.Default.LootPackage);

    public static byte[] Serialize(PackageManifest manifest) =>
        JsonSerializer.SerializeToUtf8Bytes(manifest, PackageJsonContext.Default.PackageManifest);

    public static byte[] Serialize(SignedManifest manifest) =>
        JsonSerializer.SerializeToUtf8Bytes(manifest, PackageJsonContext.Default.SignedManifest);

    // Source gen leaves omitted init lists null and doesn't check list elements for null.
    private static LootPackage Complete(LootPackage p)
    {
        var complete = p with
        {
            Maps = NoNulls(p.Maps, "maps"),
            Expansions = NoNulls(p.Expansions, "expansions"),
            Zones = NoNulls(p.Zones, "zones"),
            PointsOfInterest = NoNulls(p.PointsOfInterest, "pointsOfInterest"),
            Labels = NoNulls(p.Labels, "labels"),
            ItemCategories = NoNulls(p.ItemCategories, "itemCategories"),
            Items = NoNulls(p.Items, "items"),
            SpawnAreas = NoNulls(p.SpawnAreas, "spawnAreas"),
            RecipeCategories = NoNulls(p.RecipeCategories, "recipeCategories"),
            Recipes = NoNulls(p.Recipes, "recipes"),
        };

        foreach (var s in complete.SpawnAreas)
        {
            NoNulls(s.Photos, "spawnAreas.photos");
        }

        foreach (var r in complete.Recipes)
        {
            NoNulls(r.Ingredients, "recipes.ingredients");
            NoNulls(r.Components, "recipes.components");
        }

        return complete;
    }

    private static IReadOnlyList<T> NoNulls<T>(IReadOnlyList<T>? list, string name)
        where T : class
    {
        if (list is null)
        {
            return [];
        }

        foreach (var item in list)
        {
            if (item is null)
            {
                throw new JsonException($"'{name}' contains null.");
            }
        }

        return list;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    AllowDuplicateProperties = false,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    MaxDepth = 16)]
[JsonSerializable(typeof(LootPackage))]
[JsonSerializable(typeof(PackageManifest))]
[JsonSerializable(typeof(SignedManifest))]
internal sealed partial class PackageJsonContext : JsonSerializerContext;
