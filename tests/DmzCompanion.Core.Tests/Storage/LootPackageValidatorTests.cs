using DmzCompanion.Core.Loot;

namespace DmzCompanion.Core.Tests.Storage;

public sealed class LootPackageValidatorTests
{
    [Fact]
    public void SamplePackage_IsValid() => Assert.Empty(LootPackageValidator.Validate(SamplePackage.Create()));

    [Fact]
    public void ExpansionOnUnknownMap_IsRejected()
    {
        var package = SamplePackage.Create() with
        {
            Expansions = [new MapExpansion("russia", "warzone", "Russia", ExpansionStatus.Announced, new MapRect(0, 0, 1, 1))],
        };

        Assert.Contains(LootPackageValidator.Validate(package), e => e.Contains("map 'warzone'", StringComparison.Ordinal));
    }

    [Fact]
    public void ContentInExpansionOfAnotherMap_IsRejected()
    {
        var other = SamplePackage.Map with { Id = "other" };
        var package = SamplePackage.Create() with
        {
            Maps = [SamplePackage.Map, other],
            PointsOfInterest = [new PointOfInterest("x", "other", "X", PoiKind.Building, new MapPoint(0.5, 0.5), null, "russia")],
        };

        Assert.Contains(LootPackageValidator.Validate(package), e => e.Contains("belongs to map 'dmz'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1.2, 0.5)]
    [InlineData(-0.1, 0.5)]
    [InlineData(double.NaN, 0.5)]
    public void PositionOutsideNormalizedRange_IsRejected(double x, double y)
    {
        var package = SamplePackage.Create() with
        {
            PointsOfInterest = [new PointOfInterest("p", "dmz", "P", PoiKind.Building, new MapPoint(x, y))],
        };

        Assert.Contains(LootPackageValidator.Validate(package), e => e.Contains("normalized", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://example.com/map.jpg")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:pw@example.com/map.jpg")]
    [InlineData("maps/dmz.jpg")]
    public void NonHttpsImageUrl_IsRejected(string url)
    {
        var package = SamplePackage.Create() with { Maps = [SamplePackage.Map with { ImageUrl = url }] };

        Assert.Contains(LootPackageValidator.Validate(package), e => e.Contains("https", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../secret.jpg")]
    [InlineData("/abs.jpg")]
    [InlineData("a//b.jpg")]
    [InlineData("a\\b.jpg")]
    public void UnsafePhotoRef_IsRejected(string imageRef)
    {
        var package = SamplePackage.Create() with
        {
            SpawnAreas = [new SpawnArea("s", "wires", "dmz", new MapPoint(0.5, 0.5), 0.01, [new SpawnPhoto(SpawnPhotoKind.Container, imageRef, "c")])],
        };

        Assert.Contains(LootPackageValidator.Validate(package), e => e.Contains("safe relative path", StringComparison.Ordinal));
    }

    [Fact]
    public void RecipeProblems_AreAllReported()
    {
        var package = SamplePackage.Create() with
        {
            Recipes =
            [
                new CraftingRecipe("bad", "weapons", "Rifle", 11, "d", -1, [new ItemQuantity("nope", 0), new ItemQuantity("nope", 1)], []),
            ],
        };

        var errors = LootPackageValidator.Validate(package);

        Assert.Contains(errors, e => e.Contains("category 'weapons'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("tier", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("cash cost", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("ingredient 'nope' doesn't exist", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("listed twice", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateAndInvalidIds_AreRejected()
    {
        var package = SamplePackage.Create() with
        {
            ItemCategories = [new ItemCategory("part", "A", "A"), new ItemCategory("part", "B", "B"), new ItemCategory("bad id", "C", "C")],
        };

        var errors = LootPackageValidator.Validate(package);

        Assert.Contains(errors, e => e.Contains("duplicated", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("'bad id': id is invalid", StringComparison.Ordinal));
    }
}
