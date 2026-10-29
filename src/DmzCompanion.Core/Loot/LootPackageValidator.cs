using System.Globalization;
using System.Text.RegularExpressions;

namespace DmzCompanion.Core.Loot;

/// <summary>Checks a package for broken references and out-of-range values before it's cached.</summary>
public static partial class LootPackageValidator
{
    public const int MaxIdLength = 64;

    public const int MaxImageSide = 16_384;

    public static IReadOnlyList<string> Validate(LootPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var v = new Run();

        if (package.Revision < 1)
        {
            v.Add("package", "revision", "must be at least 1");
        }

        var maps = v.Ids("map", package.Maps, m => m.Id);
        foreach (var m in package.Maps)
        {
            v.Text("map", m.Id, "name", m.Name);
            if (!IsHttpsUrl(m.ImageUrl))
            {
                v.Add("map", m.Id, "image URL must be an absolute https URL");
            }

            if (m.ReferenceWidth is < 1 or > MaxImageSide || m.ReferenceHeight is < 1 or > MaxImageSide)
            {
                v.Add("map", m.Id, $"reference size must be 1 to {MaxImageSide.ToString(CultureInfo.InvariantCulture)} pixels");
            }

            if (!IsPositive(m.MetersPerPixel))
            {
                v.Add("map", m.Id, "meters per pixel must be positive");
            }
        }

        v.Ids("expansion", package.Expansions, e => e.Id);
        var expansionMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in package.Expansions)
        {
            v.Text("expansion", e.Id, "name", e.Name);
            v.Ref("expansion", e.Id, "map", e.MapId, maps);
            v.Enum("expansion", e.Id, e.Status);
            if (!e.Bounds.IsValid)
            {
                v.Add("expansion", e.Id, "bounds are invalid");
            }

            expansionMap.TryAdd(e.Id, e.MapId);
        }

        v.Ids("zone", package.Zones, z => z.Id);
        var zoneMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var z in package.Zones)
        {
            v.Text("zone", z.Id, "name", z.Name);
            v.Placement("zone", z.Id, z.MapId, z.ExpansionId, maps, expansionMap);
            if (!z.Bounds.IsValid)
            {
                v.Add("zone", z.Id, "bounds are invalid");
            }

            zoneMap.TryAdd(z.Id, z.MapId);
        }

        v.Ids("point of interest", package.PointsOfInterest, p => p.Id);
        foreach (var p in package.PointsOfInterest)
        {
            v.Text("point of interest", p.Id, "name", p.Name);
            v.Placement("point of interest", p.Id, p.MapId, p.ExpansionId, maps, expansionMap);
            v.ZoneOnMap("point of interest", p.Id, p.MapId, p.ZoneId, zoneMap);
            v.Enum("point of interest", p.Id, p.Kind);
            v.Point("point of interest", p.Id, p.Position);
        }

        v.Ids("label", package.Labels, l => l.Id);
        foreach (var l in package.Labels)
        {
            v.Text("label", l.Id, "text", l.Text);
            v.Placement("label", l.Id, l.MapId, l.ExpansionId, maps, expansionMap);
            v.Enum("label", l.Id, l.Kind);
            v.Point("label", l.Id, l.Position);
            if (!double.IsFinite(l.MinZoom) || l.MinZoom < 0
                || (l.MaxZoom is { } max && (!double.IsFinite(max) || max < l.MinZoom)))
            {
                v.Add("label", l.Id, "zoom range is invalid");
            }
        }

        var categories = v.Ids("item category", package.ItemCategories, c => c.Id);
        foreach (var c in package.ItemCategories)
        {
            v.Text("item category", c.Id, "name", c.Name);
            v.Text("item category", c.Id, "short code", c.ShortCode);
        }

        var items = v.Ids("item", package.Items, i => i.Id);
        foreach (var i in package.Items)
        {
            v.Text("item", i.Id, "name", i.Name);
            v.Ref("item", i.Id, "category", i.CategoryId, categories);
            v.Enum("item", i.Id, i.Rarity);
            if (i.Value < 0)
            {
                v.Add("item", i.Id, "value can't be negative");
            }

            if (i.StackSize < 1)
            {
                v.Add("item", i.Id, "stack size must be at least 1");
            }
        }

        v.Ids("spawn area", package.SpawnAreas, s => s.Id);
        foreach (var s in package.SpawnAreas)
        {
            v.Ref("spawn area", s.Id, "item", s.ItemId, items);
            v.Placement("spawn area", s.Id, s.MapId, s.ExpansionId, maps, expansionMap);
            v.ZoneOnMap("spawn area", s.Id, s.MapId, s.ZoneId, zoneMap);
            v.Point("spawn area", s.Id, s.Center);
            if (!MapUnits.IsNormalized(s.Radius))
            {
                v.Add("spawn area", s.Id, "radius must be 0 to 1");
            }

            if (s.Photos is null)
            {
                v.Add("spawn area", s.Id, "photos are missing");
                continue;
            }

            foreach (var photo in s.Photos)
            {
                v.Enum("spawn area", s.Id, photo.Kind);
                if (!IsSafeImageRef(photo.ImageRef))
                {
                    v.Add("spawn area", s.Id, $"photo reference '{photo.ImageRef}' isn't a safe relative path");
                }

                if (photo.Caption is null)
                {
                    v.Add("spawn area", s.Id, "photo caption is missing");
                }
            }
        }

        var recipeCategories = v.Ids("recipe category", package.RecipeCategories, c => c.Id);
        foreach (var c in package.RecipeCategories)
        {
            v.Text("recipe category", c.Id, "name", c.Name);
        }

        v.Ids("recipe", package.Recipes, r => r.Id);
        foreach (var r in package.Recipes)
        {
            v.Text("recipe", r.Id, "name", r.Name);
            v.Ref("recipe", r.Id, "category", r.CategoryId, recipeCategories);
            if (r.Description is null)
            {
                v.Add("recipe", r.Id, "description is missing");
            }

            if (r.Tier is < 0 or > LootPackage.MaxRecipeTier)
            {
                v.Add("recipe", r.Id, $"tier must be 0 to {LootPackage.MaxRecipeTier.ToString(CultureInfo.InvariantCulture)}");
            }

            if (r.CashCost < 0)
            {
                v.Add("recipe", r.Id, "cash cost can't be negative");
            }

            if (r.Ingredients is null || r.Components is null)
            {
                v.Add("recipe", r.Id, "ingredients or components are missing");
                continue;
            }

            if (r.Ingredients.Count == 0 && r.Components.Count == 0)
            {
                v.Add("recipe", r.Id, "needs at least one ingredient or component");
            }

            var seenItems = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ing in r.Ingredients)
            {
                v.Ref("recipe", r.Id, "ingredient", ing.ItemId, items);
                if (ing.Quantity < 1)
                {
                    v.Add("recipe", r.Id, $"ingredient '{ing.ItemId}' quantity must be at least 1");
                }

                if (ing.ItemId is not null && !seenItems.Add(ing.ItemId))
                {
                    v.Add("recipe", r.Id, $"ingredient '{ing.ItemId}' is listed twice");
                }
            }

            var seenComponents = new HashSet<ComponentType>();
            foreach (var comp in r.Components)
            {
                v.Enum("recipe", r.Id, comp.Component);
                if (comp.Quantity < 1)
                {
                    v.Add("recipe", r.Id, $"component {comp.Component} quantity must be at least 1");
                }

                if (!seenComponents.Add(comp.Component))
                {
                    v.Add("recipe", r.Id, $"component {comp.Component} is listed twice");
                }
            }
        }

        return v.Errors;
    }

    public static bool IsValidId(string? id) => id is { Length: > 0 and <= MaxIdLength } && IdPattern().IsMatch(id);

    /// <summary>Relative forward-slash path with no parent segments.</summary>
    public static bool IsSafeImageRef(string? path) =>
        path is { Length: > 0 and <= 260 }
        && ImageRefPattern().IsMatch(path)
        && !path.StartsWith('/')
        && path.Split('/').All(s => s.Length > 0 && s != "." && s != "..");

    public static bool IsHttpsUrl(string? url) =>
        url is { Length: > 0 and <= 2048 }
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsPositive(double value) => double.IsFinite(value) && value > 0;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Za-z0-9._/-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ImageRefPattern();

    private sealed class Run
    {
        public List<string> Errors { get; } = [];

        public void Add(string kind, string? id, string message) => Errors.Add($"{kind} '{id}': {message}");

        public HashSet<string> Ids<T>(string kind, IReadOnlyList<T>? list, Func<T, string> id)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (list is null)
            {
                Errors.Add($"{kind} list is missing");
                return seen;
            }

            foreach (var entry in list)
            {
                if (entry is null)
                {
                    Errors.Add($"{kind} list has an empty entry");
                    continue;
                }

                var value = id(entry);
                if (!IsValidId(value))
                {
                    Add(kind, value, "id is invalid");
                }
                else if (!seen.Add(value))
                {
                    Add(kind, value, "id is duplicated");
                }
            }

            return seen;
        }

        public void Text(string kind, string id, string field, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Add(kind, id, $"{field} is empty");
            }
        }

        public void Ref(string kind, string id, string field, string? target, HashSet<string> known)
        {
            if (target is null || !known.Contains(target))
            {
                Add(kind, id, $"{field} '{target}' doesn't exist");
            }
        }

        public void Placement(
            string kind,
            string id,
            string? mapId,
            string? expansionId,
            HashSet<string> maps,
            Dictionary<string, string> expansionMap)
        {
            Ref(kind, id, "map", mapId, maps);
            if (expansionId is null)
            {
                return;
            }

            if (!expansionMap.TryGetValue(expansionId, out var owner))
            {
                Add(kind, id, $"expansion '{expansionId}' doesn't exist");
            }
            else if (!string.Equals(owner, mapId, StringComparison.Ordinal))
            {
                Add(kind, id, $"expansion '{expansionId}' belongs to map '{owner}', not '{mapId}'");
            }
        }

        public void ZoneOnMap(string kind, string id, string? mapId, string? zoneId, Dictionary<string, string> zoneMap)
        {
            if (zoneId is null)
            {
                return;
            }

            if (!zoneMap.TryGetValue(zoneId, out var owner))
            {
                Add(kind, id, $"zone '{zoneId}' doesn't exist");
            }
            else if (!string.Equals(owner, mapId, StringComparison.Ordinal))
            {
                Add(kind, id, $"zone '{zoneId}' is on map '{owner}', not '{mapId}'");
            }
        }

        public void Point(string kind, string id, MapPoint point)
        {
            if (!point.IsNormalized)
            {
                Add(kind, id, "position must be normalized to 0 to 1");
            }
        }

        public void Enum<TEnum>(string kind, string id, TEnum value)
            where TEnum : struct, Enum
        {
            if (!System.Enum.IsDefined(value))
            {
                Add(kind, id, $"{typeof(TEnum).Name} value {value} is unknown");
            }
        }
    }
}
