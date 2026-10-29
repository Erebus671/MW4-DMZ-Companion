using DmzCompanion.Core.Loot;
using DmzCompanion.Core.UserState;
using Microsoft.Data.Sqlite;
using static DmzCompanion.Core.Storage.SqlHelpers;

namespace DmzCompanion.Core.Storage;

/// <summary>User-owned progress. Kept apart from package data so package updates never reset it.</summary>
public sealed class UserStateStore(CacheDatabase database)
{
    private const string CashKey = "cash";

    private readonly CacheDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public Task<int> GetCollectedCountAsync(string itemId, CancellationToken cancellationToken = default)
    {
        RequireId(itemId, nameof(itemId));
        return _database.RunAsync(
            "read collected count",
            async (c, ct) =>
            {
                await using var command = Command(
                    c, null, "SELECT count FROM user_collected WHERE item_id = $id;", ("$id", itemId));
                var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
                return value is long count ? (int)count : 0;
            },
            cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, int>> GetCollectedCountsAsync(CancellationToken cancellationToken = default) =>
        _database.RunAsync(
            "read collected counts",
            (c, ct) => ReadCountsAsync(c, "SELECT item_id, count FROM user_collected WHERE count > 0;", ct),
            cancellationToken);

    /// <summary>Atomically adds <paramref name="delta"/>, clamped to 0..<see cref="UserStateLimits.MaxCollected"/>. Returns the new count.</summary>
    public Task<int> AdjustCollectedCountAsync(string itemId, int delta, CancellationToken cancellationToken = default)
    {
        RequireId(itemId, nameof(itemId));
        return _database.RunAsync(
            "adjust collected count",
            async (c, ct) =>
            {
                await using var command = Command(
                    c,
                    null,
                    """
                    INSERT INTO user_collected (item_id, count, updated_utc)
                    VALUES ($id, MIN(MAX($delta, 0), $max), $now)
                    ON CONFLICT (item_id) DO UPDATE SET
                        count = MIN(MAX(count + $delta, 0), $max),
                        updated_utc = $now
                    RETURNING count;
                    """,
                    ("$id", itemId),
                    ("$delta", delta),
                    ("$max", UserStateLimits.MaxCollected),
                    ("$now", FormatTime(_database.Time.GetUtcNow())));
                var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
                return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            },
            cancellationToken);
    }

    public Task SetCollectedCountAsync(string itemId, int count, CancellationToken cancellationToken = default)
    {
        RequireId(itemId, nameof(itemId));
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, UserStateLimits.MaxCollected);
        return Execute(
            "set collected count",
            """
            INSERT INTO user_collected (item_id, count, updated_utc) VALUES ($id, $count, $now)
            ON CONFLICT (item_id) DO UPDATE SET count = excluded.count, updated_utc = excluded.updated_utc;
            """,
            cancellationToken,
            ("$id", itemId),
            ("$count", count),
            ("$now", FormatTime(_database.Time.GetUtcNow())));
    }

    public Task<IReadOnlyDictionary<string, int>> GetHaveCountsAsync(CancellationToken cancellationToken = default) =>
        _database.RunAsync(
            "read inventory",
            (c, ct) => ReadCountsAsync(c, "SELECT item_id, have FROM user_inventory;", ct),
            cancellationToken);

    /// <summary>Zero removes the entry.</summary>
    public Task SetHaveCountAsync(string itemId, int have, CancellationToken cancellationToken = default)
    {
        RequireId(itemId, nameof(itemId));
        ArgumentOutOfRangeException.ThrowIfNegative(have);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(have, UserStateLimits.MaxHave);
        return have == 0
            ? Execute("clear inventory count", "DELETE FROM user_inventory WHERE item_id = $id;", cancellationToken, ("$id", itemId))
            : Execute(
                "set inventory count",
                "INSERT INTO user_inventory (item_id, have) VALUES ($id, $have) ON CONFLICT (item_id) DO UPDATE SET have = excluded.have;",
                cancellationToken,
                ("$id", itemId),
                ("$have", have));
    }

    public Task<IReadOnlyDictionary<ComponentType, int>> GetComponentCountsAsync(CancellationToken cancellationToken = default) =>
        _database.RunAsync<IReadOnlyDictionary<ComponentType, int>>(
            "read components",
            async (c, ct) =>
            {
                var counts = await ReadCountsAsync(c, "SELECT component, count FROM user_components;", ct).ConfigureAwait(false);
                return counts.ToDictionary(p => ParseEnum<ComponentType>(p.Key), p => p.Value);
            },
            cancellationToken);

    public Task SetComponentCountAsync(ComponentType component, int count, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(component))
        {
            throw new ArgumentOutOfRangeException(nameof(component));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, UserStateLimits.MaxComponent);
        return Execute(
            "set component count",
            "INSERT INTO user_components (component, count) VALUES ($c, $n) ON CONFLICT (component) DO UPDATE SET count = excluded.count;",
            cancellationToken,
            ("$c", component.ToString()),
            ("$n", count));
    }

    public Task<long> GetCashAsync(CancellationToken cancellationToken = default) =>
        _database.RunAsync(
            "read cash",
            async (c, ct) =>
            {
                await using var command = Command(c, null, "SELECT value FROM user_values WHERE key = $k;", ("$k", CashKey));
                return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is long cash ? cash : 0L;
            },
            cancellationToken);

    public Task SetCashAsync(long cash, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cash);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cash, UserStateLimits.MaxCash);
        return Execute(
            "set cash",
            "INSERT INTO user_values (key, value) VALUES ($k, $v) ON CONFLICT (key) DO UPDATE SET value = excluded.value;",
            cancellationToken,
            ("$k", CashKey),
            ("$v", cash));
    }

    /// <summary>Tracked recipe ids in the order they were tracked.</summary>
    public Task<IReadOnlyList<string>> GetTrackedRecipeIdsAsync(CancellationToken cancellationToken = default) =>
        _database.RunAsync<IReadOnlyList<string>>(
            "read tracked recipes",
            async (c, ct) =>
            {
                await using var command = Command(c, null, "SELECT recipe_id FROM user_tracked_recipes ORDER BY rowid;");
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                var ids = new List<string>();
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    ids.Add(reader.GetString(0));
                }

                return ids;
            },
            cancellationToken);

    public Task SetRecipeTrackedAsync(string recipeId, bool tracked, CancellationToken cancellationToken = default)
    {
        RequireId(recipeId, nameof(recipeId));
        return tracked
            ? Execute(
                "track recipe",
                "INSERT OR IGNORE INTO user_tracked_recipes (recipe_id, tracked_utc) VALUES ($id, $now);",
                cancellationToken,
                ("$id", recipeId),
                ("$now", FormatTime(_database.Time.GetUtcNow())))
            : Execute("untrack recipe", "DELETE FROM user_tracked_recipes WHERE recipe_id = $id;", cancellationToken, ("$id", recipeId));
    }

    public Task<IReadOnlyList<CustomGoal>> GetGoalsAsync(CancellationToken cancellationToken = default) =>
        _database.RunAsync<IReadOnlyList<CustomGoal>>(
            "read goals",
            async (c, ct) =>
            {
                using var tx = c.BeginTransaction(deferred: true);
                var requirements = new Dictionary<string, List<ItemQuantity>>(StringComparer.Ordinal);
                await using (var command = Command(
                    c, tx, "SELECT goal_id, item_id, quantity FROM user_goal_requirements ORDER BY goal_id, ord;"))
                await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var goalId = reader.GetString(0);
                        if (!requirements.TryGetValue(goalId, out var list))
                        {
                            list = [];
                            requirements[goalId] = list;
                        }

                        list.Add(new ItemQuantity(reader.GetString(1), reader.GetInt32(2)));
                    }
                }

                var goals = new List<CustomGoal>();
                await using (var command = Command(c, tx, "SELECT id, name, target FROM user_goals ORDER BY ord;"))
                await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var id = reader.GetString(0);
                        goals.Add(new CustomGoal(
                            id, reader.GetString(1), reader.GetString(2), requirements.GetValueOrDefault(id) ?? []));
                    }
                }

                tx.Commit();
                return goals;
            },
            cancellationToken);

    /// <summary>Inserts or replaces a goal. New goals go to the end of the list.</summary>
    public Task SaveGoalAsync(CustomGoal goal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(goal);
        RequireId(goal.Id, nameof(goal));
        if (string.IsNullOrWhiteSpace(goal.Name) || goal.Target is null || goal.Requirements is null)
        {
            throw new ArgumentException("Goal needs a name, target and requirements.", nameof(goal));
        }

        foreach (var req in goal.Requirements)
        {
            if (req is null || !LootPackageValidator.IsValidId(req.ItemId) || req.Quantity < 1)
            {
                throw new ArgumentException("Goal requirements need a valid item and a quantity of at least 1.", nameof(goal));
            }
        }

        return _database.RunAsync(
            "save goal",
            async (c, ct) =>
            {
                using var tx = c.BeginTransaction();
                await using (var upsert = Command(
                    c,
                    tx,
                    """
                    INSERT INTO user_goals (id, ord, name, target, created_utc)
                    VALUES ($id, (SELECT COALESCE(MAX(ord), -1) + 1 FROM user_goals), $name, $target, $now)
                    ON CONFLICT (id) DO UPDATE SET name = excluded.name, target = excluded.target;
                    """,
                    ("$id", goal.Id),
                    ("$name", goal.Name),
                    ("$target", goal.Target),
                    ("$now", FormatTime(_database.Time.GetUtcNow()))))
                {
                    await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using (var clear = Command(c, tx, "DELETE FROM user_goal_requirements WHERE goal_id = $id;", ("$id", goal.Id)))
                {
                    await clear.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                using (var insert = new Insert(c, tx, "user_goal_requirements", "goal_id", "ord", "item_id", "quantity"))
                {
                    for (var i = 0; i < goal.Requirements.Count; i++)
                    {
                        await insert.ExecAsync(ct, goal.Id, i, goal.Requirements[i].ItemId, goal.Requirements[i].Quantity)
                            .ConfigureAwait(false);
                    }
                }

                tx.Commit();
                return true;
            },
            cancellationToken);
    }

    /// <summary>Returns false when the goal didn't exist.</summary>
    public Task<bool> DeleteGoalAsync(string goalId, CancellationToken cancellationToken = default)
    {
        RequireId(goalId, nameof(goalId));
        return _database.RunAsync(
            "delete goal",
            async (c, ct) =>
            {
                await using var command = Command(c, null, "DELETE FROM user_goals WHERE id = $id;", ("$id", goalId));
                return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
            },
            cancellationToken);
    }

    private Task Execute(
        string operation,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters) =>
        _database.RunAsync(
            operation,
            async (c, ct) =>
            {
                await using var command = Command(c, null, sql, parameters);
                return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancellationToken);

    private static async Task<IReadOnlyDictionary<string, int>> ReadCountsAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken ct)
    {
        await using var command = Command(connection, null, sql);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return counts;
    }

    private static void RequireId(string? id, string paramName)
    {
        if (!LootPackageValidator.IsValidId(id))
        {
            throw new ArgumentException("Id is invalid.", paramName);
        }
    }
}
