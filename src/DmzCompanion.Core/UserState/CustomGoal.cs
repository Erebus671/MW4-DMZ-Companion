using DmzCompanion.Core.Loot;

namespace DmzCompanion.Core.UserState;

/// <summary>User-defined goal in the shared goal pool, next to tracked recipes.</summary>
public sealed record CustomGoal(string Id, string Name, string Target, IReadOnlyList<ItemQuantity> Requirements);
