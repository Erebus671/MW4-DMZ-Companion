using DmzCompanion.Core.Loot;
using DmzCompanion.Core.Storage;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Tests.Storage;

public sealed class LootPackageStoreTests : CacheTestBase
{
    private readonly CaptureLogger<LootPackageStore> _logger = new();

    [Fact]
    public async Task EmptyCache_HasNoPackage()
    {
        using var db = await OpenAsync(Ct);
        var store = new LootPackageStore(db, _logger);

        Assert.Null(await store.LoadAsync(Ct));
        Assert.Null(await store.GetInfoAsync(Ct));
    }

    [Fact]
    public async Task Package_RoundTrips()
    {
        var package = SamplePackage.Create();
        using var db = await OpenAsync(Ct);
        var store = new LootPackageStore(db, _logger);

        await store.ReplaceAsync(package, Ct);
        var loaded = await store.LoadAsync(Ct);

        Assert.NotNull(loaded);
        Assert.Equivalent(package, loaded, strict: true);
        Assert.Equal(package.Items.Select(i => i.Id), loaded.Items.Select(i => i.Id));
        Assert.Equal(new LootPackageInfo(1, SamplePackage.Published, Time.Now), await store.GetInfoAsync(Ct));
    }

    [Fact]
    public async Task Expansion_StaysAttachedToExistingMap()
    {
        using var db = await OpenAsync(Ct);
        var store = new LootPackageStore(db, _logger);
        await store.ReplaceAsync(SamplePackage.Create(), Ct);

        var loaded = (await store.LoadAsync(Ct))!;

        var map = Assert.Single(loaded.Maps);
        var russia = Assert.Single(loaded.ExpansionsOf(map.Id));
        Assert.Equal(ExpansionStatus.Announced, russia.Status);
        Assert.All(
            loaded.PointsOfInterest.Where(p => p.ExpansionId == russia.Id),
            p => Assert.True(p.MapId == map.Id && russia.Bounds.Contains(p.Position)));
        Assert.Equal("russia", loaded.SpawnAreasOf("gpu").Single().ExpansionId);
        Assert.Contains(loaded.LabelsOf(map.Id), l => l.ExpansionId == "russia");
    }

    [Fact]
    public async Task Replace_RechecksMapImageRecalibration()
    {
        using var db = await OpenAsync(Ct);
        var store = new LootPackageStore(db, _logger);
        var cache = new MapImageCache(db, new CaptureLogger<MapImageCache>());
        await store.ReplaceAsync(SamplePackage.Create(), Ct);
        await cache.ApplyCheckAsync(SamplePackage.Map, new(MapImageCheckStatus.Downloaded, null, 1440, 1404), Ct);

        var resized = SamplePackage.Create(revision: 2) with { Maps = [SamplePackage.Map with { ReferenceWidth = 2048 }] };
        await store.ReplaceAsync(resized, Ct);
        Assert.True((await cache.GetAsync("dmz", Ct))!.NeedsRecalibration);
        Assert.Single(_logger.Records, r => r.Level == LogLevel.Warning);

        await store.ReplaceAsync(SamplePackage.Create(revision: 3), Ct);
        Assert.False((await cache.GetAsync("dmz", Ct))!.NeedsRecalibration);
    }

    [Fact]
    public async Task Replace_SwapsContentsWholesale()
    {
        using var db = await OpenAsync(Ct);
        var store = new LootPackageStore(db, _logger);
        await store.ReplaceAsync(SamplePackage.Create(), Ct);

        var smaller = SamplePackage.Create(revision: 2) with { Expansions = [], Zones = [], PointsOfInterest = [], Labels = [], SpawnAreas = [], Recipes = [] };
        await store.ReplaceAsync(smaller, Ct);

        var loaded = (await store.LoadAsync(Ct))!;
        Assert.Equal(2, loaded.Revision);
        Assert.Empty(loaded.Expansions);
        Assert.Empty(loaded.SpawnAreas);
        Assert.Equal(3, loaded.Items.Count);
    }

    [Fact]
    public async Task InvalidPackage_IsRejectedAndOldOneKept()
    {
        using var db = await OpenAsync(Ct);
        var store = new LootPackageStore(db, _logger);
        await store.ReplaceAsync(SamplePackage.Create(), Ct);

        var bad = SamplePackage.Create(revision: 2) with { Items = [new LootItem("wires", "W", "missing", Rarity.Common, 1, 1)] };
        var ex = await Assert.ThrowsAsync<InvalidLootPackageException>(() => store.ReplaceAsync(bad, Ct));

        Assert.NotEmpty(ex.Errors);
        Assert.Equal(1, (await store.GetInfoAsync(Ct))!.Revision);
        Assert.Contains(_logger.Records, r => r.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task UnknownStoredEnum_IsReportedAsStorageFailure()
    {
        using var db = await OpenAsync(Ct);
        var store = new LootPackageStore(db, _logger);
        await store.ReplaceAsync(SamplePackage.Create(), Ct);
        await using (var c = await db.OpenConnectionAsync(Ct))
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "UPDATE items SET rarity = 'Mythic' WHERE id = 'gpu';";
            await cmd.ExecuteNonQueryAsync(Ct);
        }

        var ex = await Assert.ThrowsAsync<LootCacheException>(() => store.LoadAsync(Ct));

        Assert.IsType<InvalidDataException>(ex.InnerException);
        Assert.Contains(DbLogger.Records, r => r.Level == LogLevel.Error);
    }

    [Fact]
    public async Task UnverifiedRecipe_RoundTrips()
    {
        var sample = SamplePackage.Create();
        var package = sample with { Recipes = [sample.Recipes[0] with { Unverified = true }, sample.Recipes[1]] };
        using var db = await OpenAsync(Ct);
        var store = new LootPackageStore(db, _logger);

        await store.ReplaceAsync(package, Ct);
        var loaded = (await store.LoadAsync(Ct))!;

        Assert.Equal([true, false], loaded.Recipes.Select(r => r.Unverified));
    }

    [Theory]
    [InlineData(2, true, 2)]
    [InlineData(3, false, 3)]
    [InlineData(1, false, 3)]
    public async Task TryInstallNewer_OnlyInstallsHigherRevisions(long revision, bool expected, long kept)
    {
        using var db = await OpenAsync(Ct);
        var store = new LootPackageStore(db, _logger);
        await store.ReplaceAsync(SamplePackage.Create(expected ? 1 : 3), Ct);

        var installed = await store.TryInstallNewerAsync(SamplePackage.Create(revision), Ct);

        Assert.Equal(expected, installed);
        Assert.Equal(kept, (await store.GetInfoAsync(Ct))!.Revision);
    }

    [Fact]
    public async Task TryInstallNewer_InstallsIntoEmptyCache()
    {
        using var db = await OpenAsync(Ct);
        var store = new LootPackageStore(db, _logger);

        Assert.True(await store.TryInstallNewerAsync(SamplePackage.Create(5), Ct));
        Assert.Equal(5, (await store.GetInfoAsync(Ct))!.Revision);
    }
}
