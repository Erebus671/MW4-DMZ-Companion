namespace DmzCompanion.Core.Loot;

public sealed record ItemCategory(string Id, string Name, string ShortCode);

public sealed record LootItem(
    string Id,
    string Name,
    string CategoryId,
    Rarity Rarity,
    int Value,
    int StackSize,
    string? Kind = null);

/// <summary>Screenshot of a container or surface the item spawns in or on. <paramref name="ImageRef"/> is relative to the package.</summary>
public sealed record SpawnPhoto(SpawnPhotoKind Kind, string ImageRef, string Caption);

/// <summary>
/// Area an item spawns somewhere within; the exact spot changes each match.
/// <paramref name="Radius"/> is a fraction of the map's reference width.
/// </summary>
public sealed record SpawnArea(
    string Id,
    string ItemId,
    string MapId,
    MapPoint Center,
    double Radius,
    IReadOnlyList<SpawnPhoto> Photos,
    string? ZoneId = null,
    string? ExpansionId = null);
