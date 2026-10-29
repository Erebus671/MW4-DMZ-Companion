using DmzCompanion.Core.Storage;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Tests.Storage;

public sealed class MapImageCacheTests : CacheTestBase
{
    private static readonly DateTimeOffset LastModified = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private readonly CaptureLogger<MapImageCache> _logger = new();

    [Fact]
    public async Task FirstDownload_StoresMetadata()
    {
        using var db = await OpenAsync(Ct);
        var cache = new MapImageCache(db, _logger);

        var action = await cache.ApplyCheckAsync(SamplePackage.Map, Downloaded(1440, 1404), Ct);

        Assert.Equal(MapImageAction.ReplaceCache, action);
        Assert.Equal(
            new MapImageInfo("dmz", SamplePackage.MapImageUrl, LastModified, Time.Now, 1440, 1404, false),
            await cache.GetAsync("dmz", Ct));
    }

    [Fact]
    public async Task NotModified_OnlyUpdatesLastChecked()
    {
        using var db = await OpenAsync(Ct);
        var cache = new MapImageCache(db, _logger);
        await cache.ApplyCheckAsync(SamplePackage.Map, Downloaded(1440, 1404), Ct);
        Time.Now += TimeSpan.FromDays(40);

        var action = await cache.ApplyCheckAsync(SamplePackage.Map, new MapImageCheckResult(MapImageCheckStatus.NotModified), Ct);

        Assert.Equal(MapImageAction.KeepCache, action);
        var info = (await cache.GetAsync("dmz", Ct))!;
        Assert.Equal(Time.Now, info.LastCheckedUtc);
        Assert.Equal(LastModified, info.LastModified);
    }

    [Fact]
    public async Task FailedFetch_KeepsCacheUnchanged()
    {
        using var db = await OpenAsync(Ct);
        var cache = new MapImageCache(db, _logger);
        await cache.ApplyCheckAsync(SamplePackage.Map, Downloaded(1440, 1404), Ct);
        var before = await cache.GetAsync("dmz", Ct);
        Time.Now += TimeSpan.FromDays(40);

        var action = await cache.ApplyCheckAsync(SamplePackage.Map, new MapImageCheckResult(MapImageCheckStatus.Failed), Ct);

        Assert.Equal(MapImageAction.KeepCache, action);
        Assert.Equal(before, await cache.GetAsync("dmz", Ct));
        Assert.DoesNotContain(_logger.Records, r => r.Level > LogLevel.Debug);
    }

    [Fact]
    public async Task DifferentSize_KeepsOldCopyAndFlagsRecalibration()
    {
        using var db = await OpenAsync(Ct);
        var cache = new MapImageCache(db, _logger);
        await cache.ApplyCheckAsync(SamplePackage.Map, Downloaded(1440, 1404), Ct);

        var action = await cache.ApplyCheckAsync(SamplePackage.Map, Downloaded(2048, 1996), Ct);

        Assert.Equal(MapImageAction.KeepCacheNeedsRecalibration, action);
        var info = (await cache.GetAsync("dmz", Ct))!;
        Assert.Equal((1440, 1404, true), (info.Width, info.Height, info.NeedsRecalibration));
        Assert.Contains(_logger.Records, r => r.Level == LogLevel.Warning);

        using var reopened = await OpenAsync(Ct);
        Assert.True((await new MapImageCache(reopened, _logger).GetAsync("dmz", Ct))!.NeedsRecalibration);
    }

    [Fact]
    public async Task MatchingDownload_ClearsRecalibrationFlag()
    {
        using var db = await OpenAsync(Ct);
        var cache = new MapImageCache(db, _logger);
        await cache.ApplyCheckAsync(SamplePackage.Map, Downloaded(1440, 1404), Ct);
        await cache.ApplyCheckAsync(SamplePackage.Map, Downloaded(800, 600), Ct);

        await cache.ApplyCheckAsync(SamplePackage.Map, Downloaded(1440, 1404), Ct);

        Assert.False((await cache.GetAsync("dmz", Ct))!.NeedsRecalibration);
    }

    [Fact]
    public async Task FirstDownloadWithWrongSize_IsUsedButFlagged()
    {
        using var db = await OpenAsync(Ct);
        var cache = new MapImageCache(db, _logger);

        var action = await cache.ApplyCheckAsync(SamplePackage.Map, Downloaded(800, 600), Ct);

        Assert.Equal(MapImageAction.ReplaceCacheNeedsRecalibration, action);
        Assert.True((await cache.GetAsync("dmz", Ct))!.NeedsRecalibration);
    }

    [Fact]
    public void RefreshPolicy_ChecksEvery31DaysOrOnRequest()
    {
        var now = Time.Now;
        var cached = new MapImageInfo("dmz", SamplePackage.MapImageUrl, LastModified, now, 1440, 1404, false);
        var map = SamplePackage.Map;

        Assert.True(MapImageRefreshPolicy.IsCheckDue(null, map, now, userRequested: false));
        Assert.False(MapImageRefreshPolicy.IsCheckDue(cached, map, now + TimeSpan.FromDays(30), userRequested: false));
        Assert.True(MapImageRefreshPolicy.IsCheckDue(cached, map, now + TimeSpan.FromDays(31), userRequested: false));
        Assert.True(MapImageRefreshPolicy.IsCheckDue(cached, map, now, userRequested: true));
        Assert.True(MapImageRefreshPolicy.IsCheckDue(cached, map, now - TimeSpan.FromDays(1), userRequested: false));
        Assert.True(MapImageRefreshPolicy.IsCheckDue(cached, map with { ImageUrl = "https://example.com/new.jpg" }, now, userRequested: false));
        Assert.Equal(LastModified, MapImageRefreshPolicy.IfModifiedSince(cached, map));
        Assert.Null(MapImageRefreshPolicy.IfModifiedSince(cached, map with { ImageUrl = "https://example.com/new.jpg" }));
    }

    private static MapImageCheckResult Downloaded(int width, int height) =>
        new(MapImageCheckStatus.Downloaded, LastModified, width, height);
}
