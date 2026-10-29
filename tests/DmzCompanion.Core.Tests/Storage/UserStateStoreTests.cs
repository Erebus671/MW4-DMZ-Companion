using DmzCompanion.Core.Loot;
using DmzCompanion.Core.Storage;
using DmzCompanion.Core.UserState;

namespace DmzCompanion.Core.Tests.Storage;

public sealed class UserStateStoreTests : CacheTestBase
{
    [Fact]
    public async Task CollectedCount_AdjustsAndPersists()
    {
        using (var db = await OpenAsync(Ct))
        {
            var store = new UserStateStore(db);
            Assert.Equal(0, await store.GetCollectedCountAsync("wires", Ct));
            Assert.Equal(1, await store.AdjustCollectedCountAsync("wires", 1, Ct));
            Assert.Equal(4, await store.AdjustCollectedCountAsync("wires", 3, Ct));
            Assert.Equal(2, await store.AdjustCollectedCountAsync("wires", -2, Ct));
        }

        using var reopened = await OpenAsync(Ct);
        var again = new UserStateStore(reopened);
        Assert.Equal(2, await again.GetCollectedCountAsync("wires", Ct));
        Assert.Equal(new Dictionary<string, int> { ["wires"] = 2 }, await again.GetCollectedCountsAsync(Ct));
    }

    [Fact]
    public async Task CollectedCount_IsClamped()
    {
        using var db = await OpenAsync(Ct);
        var store = new UserStateStore(db);

        Assert.Equal(0, await store.AdjustCollectedCountAsync("gpu", -5, Ct));
        Assert.Equal(UserStateLimits.MaxCollected, await store.AdjustCollectedCountAsync("gpu", int.MaxValue, Ct));
        Assert.Equal(UserStateLimits.MaxCollected, await store.AdjustCollectedCountAsync("gpu", 1, Ct));
        Assert.Equal(0, await store.AdjustCollectedCountAsync("gpu", int.MinValue, Ct));
    }

    [Fact]
    public async Task CollectedCount_ConcurrentAdjustmentsAreAtomic()
    {
        using var db = await OpenAsync(Ct);
        var store = new UserStateStore(db);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => store.AdjustCollectedCountAsync("fuel", 1, Ct)));

        Assert.Equal(20, await store.GetCollectedCountAsync("fuel", Ct));
    }

    [Fact]
    public async Task SetCollectedCount_ValidatesRange()
    {
        using var db = await OpenAsync(Ct);
        var store = new UserStateStore(db);

        await store.SetCollectedCountAsync("wires", 12, Ct);

        Assert.Equal(12, await store.GetCollectedCountAsync("wires", Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SetCollectedCountAsync("wires", -1, Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SetCollectedCountAsync("wires", UserStateLimits.MaxCollected + 1, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AdjustCollectedCountAsync("bad id", 1, Ct));
    }

    [Fact]
    public async Task UserState_SurvivesPackageReplace()
    {
        using var db = await OpenAsync(Ct);
        var packages = new LootPackageStore(db, new CaptureLogger<LootPackageStore>());
        var store = new UserStateStore(db);
        await packages.ReplaceAsync(SamplePackage.Create(), Ct);
        await store.AdjustCollectedCountAsync("gpu", 3, Ct);
        await store.SetRecipeTrackedAsync("plate-t2", true, Ct);

        await packages.ReplaceAsync(SamplePackage.Create(revision: 2) with { Recipes = [] }, Ct);

        Assert.Equal(3, await store.GetCollectedCountAsync("gpu", Ct));
        Assert.Equal(["plate-t2"], await store.GetTrackedRecipeIdsAsync(Ct));
    }

    [Fact]
    public async Task TrackedRecipes_KeepOrderAndIgnoreDuplicates()
    {
        using var db = await OpenAsync(Ct);
        var store = new UserStateStore(db);

        await store.SetRecipeTrackedAsync("b", true, Ct);
        await store.SetRecipeTrackedAsync("a", true, Ct);
        await store.SetRecipeTrackedAsync("b", true, Ct);
        await store.SetRecipeTrackedAsync("c", true, Ct);
        await store.SetRecipeTrackedAsync("a", false, Ct);

        Assert.Equal(["b", "c"], await store.GetTrackedRecipeIdsAsync(Ct));
    }

    [Fact]
    public async Task Goals_SaveUpdateAndDelete()
    {
        using var db = await OpenAsync(Ct);
        var store = new UserStateStore(db);
        var first = new CustomGoal("g1", "Plates", "Armor", [new ItemQuantity("wires", 4)]);
        var second = new CustomGoal("g2", "Fuel", "Truck", [new ItemQuantity("fuel", 2), new ItemQuantity("wires", 1)]);

        await store.SaveGoalAsync(first, Ct);
        await store.SaveGoalAsync(second, Ct);
        await store.SaveGoalAsync(first with { Name = "Plates v2", Requirements = [new ItemQuantity("gpu", 1)] }, Ct);

        var goals = await store.GetGoalsAsync(Ct);
        Assert.Equal(["g1", "g2"], goals.Select(g => g.Id));
        Assert.Equal("Plates v2", goals[0].Name);
        Assert.Equal([new ItemQuantity("gpu", 1)], goals[0].Requirements);
        Assert.Equal(second.Requirements, goals[1].Requirements);

        Assert.True(await store.DeleteGoalAsync("g1", Ct));
        Assert.False(await store.DeleteGoalAsync("g1", Ct));
        Assert.Equal(["g2"], (await store.GetGoalsAsync(Ct)).Select(g => g.Id));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveGoalAsync(new CustomGoal("g3", "x", "y", [new ItemQuantity("wires", 0)]), Ct));
    }

    [Fact]
    public async Task InventoryComponentsAndCash_RoundTrip()
    {
        using var db = await OpenAsync(Ct);
        var store = new UserStateStore(db);

        await store.SetHaveCountAsync("wires", 5, Ct);
        await store.SetHaveCountAsync("fuel", 2, Ct);
        await store.SetHaveCountAsync("fuel", 0, Ct);
        await store.SetComponentCountAsync(ComponentType.Electronics, 40, Ct);
        await store.SetCashAsync(UserStateLimits.MaxCash, Ct);

        Assert.Equal(new Dictionary<string, int> { ["wires"] = 5 }, await store.GetHaveCountsAsync(Ct));
        Assert.Equal(new Dictionary<ComponentType, int> { [ComponentType.Electronics] = 40 }, await store.GetComponentCountsAsync(Ct));
        Assert.Equal(UserStateLimits.MaxCash, await store.GetCashAsync(Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SetCashAsync(UserStateLimits.MaxCash + 1, Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SetHaveCountAsync("wires", UserStateLimits.MaxHave + 1, Ct));
    }
}
