namespace DmzCompanion.Core.Loot;

/// <summary>Normalized position: 0 to 1 across the map's reference image, origin top-left.</summary>
public readonly record struct MapPoint(double X, double Y)
{
    public bool IsNormalized => MapUnits.IsNormalized(X) && MapUnits.IsNormalized(Y);

    /// <summary>Pixel position on an image of the given size.</summary>
    public (double X, double Y) ToPixels(int width, int height) => (X * width, Y * height);
}

/// <summary>Axis-aligned area in normalized coordinates.</summary>
public readonly record struct MapRect(double MinX, double MinY, double MaxX, double MaxY)
{
    public bool IsValid =>
        MapUnits.IsNormalized(MinX) && MapUnits.IsNormalized(MinY)
        && MapUnits.IsNormalized(MaxX) && MapUnits.IsNormalized(MaxY)
        && MaxX > MinX && MaxY > MinY;

    public bool Contains(MapPoint point) =>
        point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;
}

public static class MapUnits
{
    public static bool IsNormalized(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
}
