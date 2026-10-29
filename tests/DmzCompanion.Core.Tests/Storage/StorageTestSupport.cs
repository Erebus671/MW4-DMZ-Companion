using DmzCompanion.Core.Loot;
using DmzCompanion.Core.Storage;

namespace DmzCompanion.Core.Tests.Storage;

internal static class SamplePackage
{
    public const string MapImageUrl = "https://www.gamesatlas.com/images/cod-warzone/maps/warzone-dmz-hajin-map-layout.jpg";

    public static readonly DateTimeOffset Published = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    public static GameMap Map => new("dmz", "DMZ", MapImageUrl, 1440, 1404, 1.5);

    /// <summary>DMZ map with an announced Russia expansion attached to it.</summary>
    public static LootPackage Create(long revision = 1) => new()
    {
        Revision = revision,
        PublishedUtc = Published,
        Maps = [Map],
        Expansions = [new MapExpansion("russia", "dmz", "Russia", ExpansionStatus.Announced, new MapRect(0.8, 0, 1, 0.3), "Season 1")],
        Zones =
        [
            new Zone("old-town", "dmz", "Old Town", new MapRect(0.1, 0.1, 0.4, 0.4)),
            new Zone("russia-border", "dmz", "Border", new MapRect(0.82, 0.02, 0.98, 0.28), "russia"),
        ],
        PointsOfInterest =
        [
            new PointOfInterest("garage", "dmz", "Garage", PoiKind.Building, new MapPoint(0.2, 0.25), "old-town"),
            new PointOfInterest("exfil-north", "dmz", "North Exfil", PoiKind.Extract, new MapPoint(0.5, 0.05)),
            new PointOfInterest("checkpoint", "dmz", "Checkpoint", PoiKind.PointOfInterest, new MapPoint(0.9, 0.1), "russia-border", "russia"),
        ],
        Labels =
        [
            new MapLabel("lbl-old-town", "dmz", LabelKind.Zone, "OLD TOWN", new MapPoint(0.25, 0.2), 0, 6),
            new MapLabel("lbl-garage", "dmz", LabelKind.Building, "Garage", new MapPoint(0.2, 0.26), 1.6),
            new MapLabel("lbl-border", "dmz", LabelKind.Zone, "BORDER", new MapPoint(0.9, 0.15), 0, 6, "russia"),
        ],
        ItemCategories = [new ItemCategory("part", "Parts", "PRT"), new ItemCategory("resource", "Resources", "RES")],
        Items =
        [
            new LootItem("wires", "Copper Wires", "part", Rarity.Common, 250, 5),
            new LootItem("gpu", "Graphics Card", "part", Rarity.Epic, 4000, 1, "electronics"),
            new LootItem("fuel", "Fuel Can", "resource", Rarity.Uncommon, 600, 3),
        ],
        SpawnAreas =
        [
            new SpawnArea(
                "sp-wires-garage", "wires", "dmz", new MapPoint(0.2, 0.25), 0.02,
                [
                    new SpawnPhoto(SpawnPhotoKind.Container, "spawns/wires/toolbox.jpg", "Toolbox, garage"),
                    new SpawnPhoto(SpawnPhotoKind.Surface, "spawns/wires/bench.jpg", "Workbench"),
                ],
                "old-town"),
            new SpawnArea("sp-gpu-border", "gpu", "dmz", new MapPoint(0.9, 0.1), 0.01, [], "russia-border", "russia"),
        ],
        RecipeCategories = [new RecipeCategory("plates", "Armor Plates"), new RecipeCategory("field", "Field Upgrades")],
        Recipes =
        [
            new CraftingRecipe(
                "plate-t2", "plates", "Armor Plate", 2, "Tier 2 plate.", 1500,
                [new ItemQuantity("wires", 2), new ItemQuantity("fuel", 1)],
                [new ComponentQuantity(ComponentType.Metals, 3), new ComponentQuantity(ComponentType.Synthetics, 1)]),
            new CraftingRecipe(
                "munitions", "field", "Munitions Box", 0, "Untiered.", 0,
                [],
                [new ComponentQuantity(ComponentType.Chemicals, 2)]),
        ],
    };
}

/// <summary>Fresh, initialized cache in its own temp directory per test.</summary>
public abstract class CacheTestBase : IDisposable
{
    protected CacheTestBase()
    {
        Directory = Path.Combine(Path.GetTempPath(), "dmz-tests", Guid.NewGuid().ToString("N"));
        DbPath = Path.Combine(Directory, CacheDatabase.FileName);
    }

    protected string Directory { get; }

    protected string DbPath { get; }

    internal ManualTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero));

    internal CaptureLogger<CacheDatabase> DbLogger { get; } = new();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TestFiles.DeleteDirectory(Directory);
        GC.SuppressFinalize(this);
    }

    internal CacheDatabase NewDatabase(IReadOnlyList<SchemaMigration>? migrations = null) =>
        new(DbPath, migrations ?? SchemaMigrations.All, DbLogger, Time);

    internal async Task<CacheDatabase> OpenAsync(CancellationToken cancellationToken)
    {
        var db = NewDatabase();
        await db.InitializeAsync(cancellationToken);
        return db;
    }
}
