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
    public async Task FailedFetch_DoesNotUpdateLastChecked()
    {
        using var db = await OpenAsync(Ct);
        var cache = new MapImageCache(db, _logger);
        await cache.ApplyCheckAsync(SamplePackage.Map, Downloaded(1440, 1404), Ct);
        var checkedAt = Time.Now;
        Time.Now += TimeSpan.FromDays(40);

        await cache.ApplyCheckAsync(SamplePackage.Map, new MapImageCheckResult(MapImageCheckStatus.Failed), Ct);

        var info = (await cache.GetAsync("dmz", Ct))!;
        Assert.Equal(checkedAt, info.LastCheckedUtc);
        Assert.True(MapImageRefreshPolicy.IsCheckDue(info, SamplePackage.Map, Time.Now, userRequested: false));
    }

    [Fact]
    public async Task ReferenceMatchingCachedSize_ClearsRecalibrationFlag()
    {
        using var db = await OpenAsync(Ct);
        var cache = new MapImageCache(db, _logger);
        var resized = SamplePackage.Map with { ReferenceWidth = 2048, ReferenceHeight = 1996 };
        await cache.ApplyCheckAsync(resized, Downloaded(2048, 1996), Ct);
        await cache.ApplyCheckAsync(resized with { ReferenceWidth = 1440, ReferenceHeight = 1404 }, Downloaded(2048, 1996), Ct);
        Assert.True((await cache.GetAsync("dmz", Ct))!.NeedsRecalibration);

        var notModified = new MapImageCheckResult(MapImageCheckStatus.NotModified);
        await cache.ApplyCheckAsync(SamplePackage.Map with { ReferenceWidth = 1000 }, notModified, Ct);
        Assert.True((await cache.GetAsync("dmz", Ct))!.NeedsRecalibration);

        var action = await cache.ApplyCheckAsync(resized, notModified, Ct);

        Assert.Equal(MapImageAction.KeepCache, action);
        Assert.False((await cache.GetAsync("dmz", Ct))!.NeedsRecalibration);
    }

    [Fact]
    public async Task ReferenceChangedAwayFromCachedSize_SetsRecalibrationFlag()
    {
        using var db = await OpenAsync(Ct);
        var cache = new MapImageCache(db, _logger);
        await cache.ApplyCheckAsync(SamplePackage.Map, Downloaded(1440, 1404), Ct);
        Assert.False((await cache.GetAsync("dmz", Ct))!.NeedsRecalibration);

        var notModified = new MapImageCheckResult(MapImageCheckStatus.NotModified);
        var action = await cache.ApplyCheckAsync(SamplePackage.Map with { ReferenceWidth = 2048 }, notModified, Ct);

        Assert.Equal(MapImageAction.KeepCache, action);
        Assert.True((await cache.GetAsync("dmz", Ct))!.NeedsRecalibration);
        Assert.Single(_logger.Records, r => r.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task FirstDownloadWithWrongSize_IsRejected()
    {
        using var db = await OpenAsync(Ct);
        var cache = new MapImageCache(db, _logger);

        var action = await cache.ApplyCheckAsync(SamplePackage.Map, Downloaded(800, 600), Ct);

        Assert.Equal(MapImageAction.RejectDownload, action);
        Assert.Null(await cache.GetAsync("dmz", Ct));
        var warning = Assert.Single(_logger.Records, r => r.Level == LogLevel.Warning);
        Assert.Contains("dmz", warning.Message, StringComparison.Ordinal);
        Assert.Contains("800x600", warning.Message, StringComparison.Ordinal);
        Assert.Contains("1440x1404", warning.Message, StringComparison.Ordinal);
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
