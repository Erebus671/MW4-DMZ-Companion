using DmzCompanion.Core.Loot;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using static DmzCompanion.Core.Storage.SqlHelpers;

namespace DmzCompanion.Core.Storage;

/// <summary>Metadata for the locally cached copy of a map image.</summary>
public sealed record MapImageInfo(
    string MapId,
    string Url,
    DateTimeOffset? LastModified,
    DateTimeOffset LastCheckedUtc,
    int Width,
    int Height,
    bool NeedsRecalibration);

public enum MapImageCheckStatus
{
    NotModified,
    Downloaded,
    Failed,
}

/// <summary>Outcome of a conditional fetch. Size is the downloaded image's, in pixels.</summary>
public sealed record MapImageCheckResult(
    MapImageCheckStatus Status,
    DateTimeOffset? LastModified = null,
    int Width = 0,
    int Height = 0);

public enum MapImageAction
{
    KeepCache,
    ReplaceCache,

    /// <summary>New image has a different size; keep the old copy and recalibrate overlays.</summary>
    KeepCacheNeedsRecalibration,

    /// <summary>First download has an unexpected size; discard it. See <see cref="Models.AppConstants.IssuesUrl"/>.</summary>
    RejectDownload,
}

public static class MapImageRefreshPolicy
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(31);

    public static bool IsCheckDue(MapImageInfo? cached, GameMap map, DateTimeOffset now, bool userRequested)
    {
        ArgumentNullException.ThrowIfNull(map);
        return userRequested
            || cached is null
            || !string.Equals(cached.Url, map.ImageUrl, StringComparison.Ordinal)
            || now < cached.LastCheckedUtc
            || now - cached.LastCheckedUtc >= CheckInterval;
    }

    /// <summary>If-Modified-Since value, or null when the cached copy is for another URL.</summary>
    public static DateTimeOffset? IfModifiedSince(MapImageInfo? cached, GameMap map) =>
        cached is not null && string.Equals(cached.Url, map.ImageUrl, StringComparison.Ordinal) ? cached.LastModified : null;
}

/// <summary>Tracks cached map image metadata. The app stores the image bytes and fetches over HTTP.</summary>
public sealed class MapImageCache(CacheDatabase database, ILogger<MapImageCache> logger)
{
    private readonly CacheDatabase _database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly ILogger<MapImageCache> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task<MapImageInfo?> GetAsync(string mapId, CancellationToken cancellationToken = default)
    {
        RequireId(mapId);
        return _database.RunAsync("read map image", (c, ct) => ReadAsync(c, null, mapId, ct), cancellationToken);
    }

    /// <summary>Records a check result and says what to do with the cached image file.</summary>
    public Task<MapImageAction> ApplyCheckAsync(
        GameMap map,
        MapImageCheckResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(result);
        RequireId(map.Id);
        if (result.Status == MapImageCheckStatus.Downloaded && (result.Width < 1 || result.Height < 1))
        {
            throw new ArgumentException("A downloaded image needs a positive size.", nameof(result));
        }

        if (result.Status == MapImageCheckStatus.Failed)
        {
            _logger.LogDebug("Map image check for {MapId} failed; keeping the cached copy.", map.Id);
            return Task.FromResult(MapImageAction.KeepCache);
        }

        return _database.RunAsync("update map image", (c, ct) => ApplyAsync(c, map, result, ct), cancellationToken);
    }

    private async Task<MapImageAction> ApplyAsync(
        SqliteConnection connection,
        GameMap map,
        MapImageCheckResult result,
        CancellationToken ct)
    {
        using var tx = connection.BeginTransaction();
        var now = _database.Time.GetUtcNow();
        var cached = await ReadAsync(connection, tx, map.Id, ct).ConfigureAwait(false);
        MapImageAction action;

        if (result.Status == MapImageCheckStatus.NotModified)
        {
            if (cached is null)
            {
                _logger.LogWarning("Map image for {MapId} reported not modified, but nothing is cached.", map.Id);
                return MapImageAction.KeepCache;
            }

            await Touch(connection, tx, map.Id, now, NeedsRecalibration(cached, map), ct).ConfigureAwait(false);
            action = MapImageAction.KeepCache;
        }
        else if (result.Width == map.ReferenceWidth && result.Height == map.ReferenceHeight)
        {
            await Upsert(connection, tx, map, result, now, needsRecalibration: false, ct).ConfigureAwait(false);
            action = MapImageAction.ReplaceCache;
        }
        else if (cached is null)
        {
            _logger.LogWarning(
                "First map image for {MapId} is {Width}x{Height}, expected {RefWidth}x{RefHeight}; rejected.",
                map.Id,
                result.Width,
                result.Height,
                map.ReferenceWidth,
                map.ReferenceHeight);
            return MapImageAction.RejectDownload;
        }
        else
        {
            _logger.LogWarning(
                "Map image for {MapId} is {Width}x{Height}, expected {RefWidth}x{RefHeight}; overlays need recalibration.",
                map.Id,
                result.Width,
                result.Height,
                map.ReferenceWidth,
                map.ReferenceHeight);
            await Touch(connection, tx, map.Id, now, needsRecalibration: true, ct).ConfigureAwait(false);
            action = MapImageAction.KeepCacheNeedsRecalibration;
        }

        tx.Commit();
        return action;
    }

    /// <summary>Clears a stale flag once the reference size matches the cached image.</summary>
    private bool NeedsRecalibration(MapImageInfo cached, GameMap map)
    {
        if (!cached.NeedsRecalibration)
        {
            return false;
        }

        if (cached.Width == map.ReferenceWidth && cached.Height == map.ReferenceHeight)
        {
            _logger.LogInformation("Map image for {MapId} matches the reference size again; recalibration flag cleared.", map.Id);
            return false;
        }

        return true;
    }

    private static async Task Touch(
        SqliteConnection connection,
        SqliteTransaction tx,
        string mapId,
        DateTimeOffset now,
        bool needsRecalibration,
        CancellationToken ct)
    {
        await using var command = Command(
            connection,
            tx,
            "UPDATE map_images SET last_checked_utc = $now, needs_recalibration = $recal WHERE map_id = $id;",
            ("$id", mapId),
            ("$now", FormatTime(now)),
            ("$recal", needsRecalibration ? 1 : 0));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task Upsert(
        SqliteConnection connection,
        SqliteTransaction tx,
        GameMap map,
        MapImageCheckResult result,
        DateTimeOffset now,
        bool needsRecalibration,
        CancellationToken ct)
    {
        await using var command = Command(
            connection,
            tx,
            """
            INSERT INTO map_images (map_id, url, last_modified, last_checked_utc, width, height, needs_recalibration)
            VALUES ($id, $url, $lm, $now, $w, $h, $recal)
            ON CONFLICT (map_id) DO UPDATE SET
                url = excluded.url,
                last_modified = excluded.last_modified,
                last_checked_utc = excluded.last_checked_utc,
                width = excluded.width,
                height = excluded.height,
                needs_recalibration = excluded.needs_recalibration;
            """,
            ("$id", map.Id),
            ("$url", map.ImageUrl),
            ("$lm", result.LastModified is { } lm ? FormatTime(lm) : null),
            ("$now", FormatTime(now)),
            ("$w", result.Width),
            ("$h", result.Height),
            ("$recal", needsRecalibration ? 1 : 0));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<MapImageInfo?> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction? tx,
        string mapId,
        CancellationToken ct)
    {
        await using var command = Command(
            connection,
            tx,
            """
            SELECT url, last_modified, last_checked_utc, width, height, needs_recalibration
            FROM map_images WHERE map_id = $id;
            """,
            ("$id", mapId));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var lastModified = reader.NullableString(1);
        return new MapImageInfo(
            mapId,
            reader.GetString(0),
            lastModified is null ? null : ParseTime(lastModified),
            ParseTime(reader.GetString(2)),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt64(5) != 0);
    }

    private static void RequireId(string mapId)
    {
        if (!LootPackageValidator.IsValidId(mapId))
        {
            throw new ArgumentException("Map id is invalid.", nameof(mapId));
        }
    }
}
