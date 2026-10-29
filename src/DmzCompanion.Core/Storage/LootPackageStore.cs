using DmzCompanion.Core.Loot;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using static DmzCompanion.Core.Storage.SqlHelpers;

namespace DmzCompanion.Core.Storage;

public sealed record LootPackageInfo(long Revision, DateTimeOffset PublishedUtc, DateTimeOffset InstalledUtc);

/// <summary>Cached copy of the loot data package. Replacing it never touches user state.</summary>
public sealed class LootPackageStore(CacheDatabase database, ILogger<LootPackageStore> logger)
{
    // Children first so foreign keys hold during the wipe.
    private static readonly string[] PackageTables =
    [
        "spawn_photos", "spawn_areas", "recipe_ingredients", "recipe_components", "recipes", "recipe_categories",
        "map_labels", "points_of_interest", "zones", "map_expansions", "items", "item_categories", "maps",
        "package_info",
    ];

    private readonly CacheDatabase _database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly ILogger<LootPackageStore> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Returns null when no package is cached yet.</summary>
    public Task<LootPackageInfo?> GetInfoAsync(CancellationToken cancellationToken = default) =>
        _database.RunAsync("read package info", (c, ct) => ReadInfoAsync(c, ct), cancellationToken);

    /// <summary>Returns null when no package is cached yet.</summary>
    public Task<LootPackage?> LoadAsync(CancellationToken cancellationToken = default) =>
        _database.RunAsync("load package", LoadCoreAsync, cancellationToken);

    /// <summary>Validates and atomically replaces the cached package.</summary>
    /// <exception cref="InvalidLootPackageException">The package failed validation.</exception>
    public async Task ReplaceAsync(LootPackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        var errors = LootPackageValidator.Validate(package);
        if (errors.Count > 0)
        {
            _logger.LogWarning(
                "Rejected loot package revision {Revision}: {Count} problems. First: {First}",
                package.Revision,
                errors.Count,
                errors[0]);
            throw new InvalidLootPackageException(errors);
        }

        await _database.RunAsync(
            "replace package",
            (c, ct) => WriteAsync(c, package, ct),
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Cached loot package revision {Revision}.", package.Revision);
    }

    private static async Task<LootPackageInfo?> ReadInfoAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = Command(
            connection, null, "SELECT revision, published_utc, installed_utc FROM package_info WHERE id = 1;");
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? new LootPackageInfo(reader.GetInt64(0), ParseTime(reader.GetString(1)), ParseTime(reader.GetString(2)))
            : null;
    }

    private async Task<bool> WriteAsync(SqliteConnection connection, LootPackage package, CancellationToken ct)
    {
        using var tx = connection.BeginTransaction();

        foreach (var table in PackageTables)
        {
            await using var delete = Command(connection, tx, $"DELETE FROM {table};");
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var info = Command(
            connection,
            tx,
            "INSERT INTO package_info (id, revision, published_utc, installed_utc) VALUES (1, $r, $p, $i);",
            ("$r", package.Revision),
            ("$p", FormatTime(package.PublishedUtc)),
            ("$i", FormatTime(_database.Time.GetUtcNow()))))
        {
            await info.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        using (var insert = new Insert(
            connection, tx, "maps", "id", "ord", "name", "image_url", "reference_width", "reference_height", "meters_per_pixel"))
        {
            for (var i = 0; i < package.Maps.Count; i++)
            {
                var m = package.Maps[i];
                await insert.ExecAsync(ct, m.Id, i, m.Name, m.ImageUrl, m.ReferenceWidth, m.ReferenceHeight, m.MetersPerPixel)
                    .ConfigureAwait(false);
            }
        }

        using (var insert = new Insert(
            connection, tx, "map_expansions",
            "id", "ord", "map_id", "name", "status", "min_x", "min_y", "max_x", "max_y", "release_note"))
        {
            for (var i = 0; i < package.Expansions.Count; i++)
            {
                var e = package.Expansions[i];
                var b = e.Bounds;
                await insert.ExecAsync(
                    ct, e.Id, i, e.MapId, e.Name, e.Status.ToString(), b.MinX, b.MinY, b.MaxX, b.MaxY, e.ReleaseNote)
                    .ConfigureAwait(false);
            }
        }

        using (var insert = new Insert(
            connection, tx, "zones", "id", "ord", "map_id", "expansion_id", "name", "min_x", "min_y", "max_x", "max_y"))
        {
            for (var i = 0; i < package.Zones.Count; i++)
            {
                var z = package.Zones[i];
                var b = z.Bounds;
                await insert.ExecAsync(ct, z.Id, i, z.MapId, z.ExpansionId, z.Name, b.MinX, b.MinY, b.MaxX, b.MaxY)
                    .ConfigureAwait(false);
            }
        }

        using (var insert = new Insert(
            connection, tx, "points_of_interest", "id", "ord", "map_id", "expansion_id", "zone_id", "name", "kind", "x", "y"))
        {
            for (var i = 0; i < package.PointsOfInterest.Count; i++)
            {
                var p = package.PointsOfInterest[i];
                await insert.ExecAsync(
                    ct, p.Id, i, p.MapId, p.ExpansionId, p.ZoneId, p.Name, p.Kind.ToString(), p.Position.X, p.Position.Y)
                    .ConfigureAwait(false);
            }
        }

        using (var insert = new Insert(
            connection, tx, "map_labels",
            "id", "ord", "map_id", "expansion_id", "kind", "text", "x", "y", "min_zoom", "max_zoom"))
        {
            for (var i = 0; i < package.Labels.Count; i++)
            {
                var l = package.Labels[i];
                await insert.ExecAsync(
                    ct, l.Id, i, l.MapId, l.ExpansionId, l.Kind.ToString(), l.Text, l.Position.X, l.Position.Y, l.MinZoom, l.MaxZoom)
                    .ConfigureAwait(false);
            }
        }

        using (var insert = new Insert(connection, tx, "item_categories", "id", "ord", "name", "short_code"))
        {
            for (var i = 0; i < package.ItemCategories.Count; i++)
            {
                var c = package.ItemCategories[i];
                await insert.ExecAsync(ct, c.Id, i, c.Name, c.ShortCode).ConfigureAwait(false);
            }
        }

        using (var insert = new Insert(
            connection, tx, "items", "id", "ord", "category_id", "name", "rarity", "value", "stack_size", "kind"))
        {
            for (var i = 0; i < package.Items.Count; i++)
            {
                var it = package.Items[i];
                await insert.ExecAsync(ct, it.Id, i, it.CategoryId, it.Name, it.Rarity.ToString(), it.Value, it.StackSize, it.Kind)
                    .ConfigureAwait(false);
            }
        }

        using (var areas = new Insert(
            connection, tx, "spawn_areas", "id", "ord", "item_id", "map_id", "expansion_id", "zone_id", "x", "y", "radius"))
        using (var photos = new Insert(connection, tx, "spawn_photos", "spawn_area_id", "ord", "kind", "image_ref", "caption"))
        {
            for (var i = 0; i < package.SpawnAreas.Count; i++)
            {
                var s = package.SpawnAreas[i];
                await areas.ExecAsync(ct, s.Id, i, s.ItemId, s.MapId, s.ExpansionId, s.ZoneId, s.Center.X, s.Center.Y, s.Radius)
                    .ConfigureAwait(false);
                for (var j = 0; j < s.Photos.Count; j++)
                {
                    var ph = s.Photos[j];
                    await photos.ExecAsync(ct, s.Id, j, ph.Kind.ToString(), ph.ImageRef, ph.Caption).ConfigureAwait(false);
                }
            }
        }

        using (var insert = new Insert(connection, tx, "recipe_categories", "id", "ord", "name"))
        {
            for (var i = 0; i < package.RecipeCategories.Count; i++)
            {
                var c = package.RecipeCategories[i];
                await insert.ExecAsync(ct, c.Id, i, c.Name).ConfigureAwait(false);
            }
        }

        using (var recipes = new Insert(
            connection, tx, "recipes", "id", "ord", "category_id", "name", "tier", "description", "cash_cost"))
        using (var ingredients = new Insert(connection, tx, "recipe_ingredients", "recipe_id", "ord", "item_id", "quantity"))
        using (var components = new Insert(connection, tx, "recipe_components", "recipe_id", "ord", "component", "quantity"))
        {
            for (var i = 0; i < package.Recipes.Count; i++)
            {
                var r = package.Recipes[i];
                await recipes.ExecAsync(ct, r.Id, i, r.CategoryId, r.Name, r.Tier, r.Description, r.CashCost).ConfigureAwait(false);
                for (var j = 0; j < r.Ingredients.Count; j++)
                {
                    await ingredients.ExecAsync(ct, r.Id, j, r.Ingredients[j].ItemId, r.Ingredients[j].Quantity).ConfigureAwait(false);
                }

                for (var j = 0; j < r.Components.Count; j++)
                {
                    await components.ExecAsync(ct, r.Id, j, r.Components[j].Component.ToString(), r.Components[j].Quantity)
                        .ConfigureAwait(false);
                }
            }
        }

        tx.Commit();
        return true;
    }

    private static async Task<LootPackage?> LoadCoreAsync(SqliteConnection connection, CancellationToken ct)
    {
        // One read transaction gives a consistent snapshot across tables.
        using var tx = connection.BeginTransaction(deferred: true);

        var info = await ReadInfoWithAsync(connection, tx, ct).ConfigureAwait(false);
        if (info is null)
        {
            return null;
        }

        var maps = await ReadAsync(connection, tx,
            "SELECT id, name, image_url, reference_width, reference_height, meters_per_pixel FROM maps ORDER BY ord;",
            r => new GameMap(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetDouble(5)),
            ct).ConfigureAwait(false);

        var expansions = await ReadAsync(connection, tx,
            "SELECT id, map_id, name, status, min_x, min_y, max_x, max_y, release_note FROM map_expansions ORDER BY ord;",
            r => new MapExpansion(
                r.GetString(0), r.GetString(1), r.GetString(2), ParseEnum<ExpansionStatus>(r.GetString(3)),
                new MapRect(r.GetDouble(4), r.GetDouble(5), r.GetDouble(6), r.GetDouble(7)), r.NullableString(8)),
            ct).ConfigureAwait(false);

        var zones = await ReadAsync(connection, tx,
            "SELECT id, map_id, name, min_x, min_y, max_x, max_y, expansion_id FROM zones ORDER BY ord;",
            r => new Zone(
                r.GetString(0), r.GetString(1), r.GetString(2),
                new MapRect(r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetDouble(6)), r.NullableString(7)),
            ct).ConfigureAwait(false);

        var pois = await ReadAsync(connection, tx,
            "SELECT id, map_id, name, kind, x, y, zone_id, expansion_id FROM points_of_interest ORDER BY ord;",
            r => new PointOfInterest(
                r.GetString(0), r.GetString(1), r.GetString(2), ParseEnum<PoiKind>(r.GetString(3)),
                new MapPoint(r.GetDouble(4), r.GetDouble(5)), r.NullableString(6), r.NullableString(7)),
            ct).ConfigureAwait(false);

        var labels = await ReadAsync(connection, tx,
            "SELECT id, map_id, kind, text, x, y, min_zoom, max_zoom, expansion_id FROM map_labels ORDER BY ord;",
            r => new MapLabel(
                r.GetString(0), r.GetString(1), ParseEnum<LabelKind>(r.GetString(2)), r.GetString(3),
                new MapPoint(r.GetDouble(4), r.GetDouble(5)), r.GetDouble(6), r.NullableDouble(7), r.NullableString(8)),
            ct).ConfigureAwait(false);

        var categories = await ReadAsync(connection, tx, "SELECT id, name, short_code FROM item_categories ORDER BY ord;",
            r => new ItemCategory(r.GetString(0), r.GetString(1), r.GetString(2)), ct).ConfigureAwait(false);

        var items = await ReadAsync(connection, tx,
            "SELECT id, name, category_id, rarity, value, stack_size, kind FROM items ORDER BY ord;",
            r => new LootItem(
                r.GetString(0), r.GetString(1), r.GetString(2), ParseEnum<Rarity>(r.GetString(3)),
                r.GetInt32(4), r.GetInt32(5), r.NullableString(6)),
            ct).ConfigureAwait(false);

        var photos = await ReadGroupedAsync(connection, tx,
            "SELECT spawn_area_id, kind, image_ref, caption FROM spawn_photos ORDER BY spawn_area_id, ord;",
            r => new SpawnPhoto(ParseEnum<SpawnPhotoKind>(r.GetString(1)), r.GetString(2), r.GetString(3)), ct).ConfigureAwait(false);

        var spawns = await ReadAsync(connection, tx,
            "SELECT id, item_id, map_id, x, y, radius, zone_id, expansion_id FROM spawn_areas ORDER BY ord;",
            r => new SpawnArea(
                r.GetString(0), r.GetString(1), r.GetString(2), new MapPoint(r.GetDouble(3), r.GetDouble(4)), r.GetDouble(5),
                photos.GetValueOrDefault(r.GetString(0)) ?? [], r.NullableString(6), r.NullableString(7)),
            ct).ConfigureAwait(false);

        var recipeCategories = await ReadAsync(connection, tx, "SELECT id, name FROM recipe_categories ORDER BY ord;",
            r => new RecipeCategory(r.GetString(0), r.GetString(1)), ct).ConfigureAwait(false);

        var ingredients = await ReadGroupedAsync(connection, tx,
            "SELECT recipe_id, item_id, quantity FROM recipe_ingredients ORDER BY recipe_id, ord;",
            r => new ItemQuantity(r.GetString(1), r.GetInt32(2)), ct).ConfigureAwait(false);

        var components = await ReadGroupedAsync(connection, tx,
            "SELECT recipe_id, component, quantity FROM recipe_components ORDER BY recipe_id, ord;",
            r => new ComponentQuantity(ParseEnum<ComponentType>(r.GetString(1)), r.GetInt32(2)), ct).ConfigureAwait(false);

        var recipes = await ReadAsync(connection, tx,
            "SELECT id, category_id, name, tier, description, cash_cost FROM recipes ORDER BY ord;",
            r => new CraftingRecipe(
                r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetString(4), r.GetInt64(5),
                ingredients.GetValueOrDefault(r.GetString(0)) ?? [], components.GetValueOrDefault(r.GetString(0)) ?? []),
            ct).ConfigureAwait(false);

        tx.Commit();

        return new LootPackage
        {
            Revision = info.Revision,
            PublishedUtc = info.PublishedUtc,
            Maps = maps,
            Expansions = expansions,
            Zones = zones,
            PointsOfInterest = pois,
            Labels = labels,
            ItemCategories = categories,
            Items = items,
            SpawnAreas = spawns,
            RecipeCategories = recipeCategories,
            Recipes = recipes,
        };
    }

    private static async Task<LootPackageInfo?> ReadInfoWithAsync(SqliteConnection connection, SqliteTransaction tx, CancellationToken ct)
    {
        var rows = await ReadAsync(connection, tx,
            "SELECT revision, published_utc, installed_utc FROM package_info WHERE id = 1;",
            r => new LootPackageInfo(r.GetInt64(0), ParseTime(r.GetString(1)), ParseTime(r.GetString(2))), ct).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    private static async Task<List<T>> ReadAsync<T>(
        SqliteConnection connection, SqliteTransaction tx, string sql, Func<SqliteDataReader, T> map, CancellationToken ct)
    {
        await using var command = Command(connection, tx, sql);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var list = new List<T>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(map(reader));
        }

        return list;
    }

    private static async Task<Dictionary<string, List<T>>> ReadGroupedAsync<T>(
        SqliteConnection connection, SqliteTransaction tx, string sql, Func<SqliteDataReader, T> map, CancellationToken ct)
    {
        await using var command = Command(connection, tx, sql);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var groups = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }

            list.Add(map(reader));
        }

        return groups;
    }
}
