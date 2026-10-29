namespace DmzCompanion.Core.Loot;

/// <summary>Read-only loot data shipped in the data package. List order is display order.</summary>
public sealed record LootPackage
{
    public const int MaxRecipeTier = 10;

    public required long Revision { get; init; }

    public required DateTimeOffset PublishedUtc { get; init; }

    public IReadOnlyList<GameMap> Maps { get; init; } = [];

    public IReadOnlyList<MapExpansion> Expansions { get; init; } = [];

    public IReadOnlyList<Zone> Zones { get; init; } = [];

    public IReadOnlyList<PointOfInterest> PointsOfInterest { get; init; } = [];

    public IReadOnlyList<MapLabel> Labels { get; init; } = [];

    public IReadOnlyList<ItemCategory> ItemCategories { get; init; } = [];

    public IReadOnlyList<LootItem> Items { get; init; } = [];

    public IReadOnlyList<SpawnArea> SpawnAreas { get; init; } = [];

    public IReadOnlyList<RecipeCategory> RecipeCategories { get; init; } = [];

    public IReadOnlyList<CraftingRecipe> Recipes { get; init; } = [];

    public IEnumerable<MapExpansion> ExpansionsOf(string mapId) =>
        Expansions.Where(e => string.Equals(e.MapId, mapId, StringComparison.Ordinal));

    public IEnumerable<MapLabel> LabelsOf(string mapId) =>
        Labels.Where(l => string.Equals(l.MapId, mapId, StringComparison.Ordinal));

    public IEnumerable<SpawnArea> SpawnAreasOf(string itemId) =>
        SpawnAreas.Where(s => string.Equals(s.ItemId, itemId, StringComparison.Ordinal));

    public IEnumerable<CraftingRecipe> RecipesUsing(string itemId) =>
        Recipes.Where(r => r.Ingredients.Any(i => string.Equals(i.ItemId, itemId, StringComparison.Ordinal)));
}
