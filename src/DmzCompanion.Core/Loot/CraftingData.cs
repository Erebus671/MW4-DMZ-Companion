namespace DmzCompanion.Core.Loot;

/// <summary>3D Printer category. Weapons aren't craftable, so there's no weapon category.</summary>
public sealed record RecipeCategory(string Id, string Name);

public sealed record ItemQuantity(string ItemId, int Quantity);

public sealed record ComponentQuantity(ComponentType Component, int Quantity);

/// <summary>
/// 3D Printer recipe. <paramref name="Tier"/> 0 means untiered.
/// <paramref name="Unverified"/> marks a recipe not yet confirmed in game (q-077).
/// </summary>
public sealed record CraftingRecipe(
    string Id,
    string CategoryId,
    string Name,
    int Tier,
    string Description,
    long CashCost,
    IReadOnlyList<ItemQuantity> Ingredients,
    IReadOnlyList<ComponentQuantity> Components,
    bool Unverified = false);
