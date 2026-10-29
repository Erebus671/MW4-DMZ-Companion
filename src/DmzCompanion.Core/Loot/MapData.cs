namespace DmzCompanion.Core.Loot;

/// <summary>
/// A playable map. Overlay coordinates are normalized against the reference image size,
/// so a cached image with other dimensions needs recalibration.
/// </summary>
public sealed record GameMap(
    string Id,
    string Name,
    string ImageUrl,
    int ReferenceWidth,
    int ReferenceHeight,
    double MetersPerPixel);

/// <summary>New area added to an existing map. Content in it references both the map and this expansion.</summary>
public sealed record MapExpansion(
    string Id,
    string MapId,
    string Name,
    ExpansionStatus Status,
    MapRect Bounds,
    string? ReleaseNote = null);

public sealed record Zone(string Id, string MapId, string Name, MapRect Bounds, string? ExpansionId = null);

/// <summary>Building, extract, or other point of interest.</summary>
public sealed record PointOfInterest(
    string Id,
    string MapId,
    string Name,
    PoiKind Kind,
    MapPoint Position,
    string? ZoneId = null,
    string? ExpansionId = null);

/// <summary>Entry in the toggleable label layer. <paramref name="MaxZoom"/> null means no upper limit.</summary>
public sealed record MapLabel(
    string Id,
    string MapId,
    LabelKind Kind,
    string Text,
    MapPoint Position,
    double MinZoom = 0,
    double? MaxZoom = null,
    string? ExpansionId = null);
