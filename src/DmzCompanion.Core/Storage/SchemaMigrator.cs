using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Storage;

/// <summary>Applies forward-only migrations tracked in PRAGMA user_version, one transaction per step.</summary>
internal static class SchemaMigrator
{
    public static async Task MigrateAsync(
        SqliteConnection connection,
        IReadOnlyList<SchemaMigration> migrations,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < migrations.Count; i++)
        {
            if (migrations[i].Version != i + 1)
            {
                throw new InvalidOperationException($"Migration at index {i} must be version {i + 1}.");
            }
        }

        var target = migrations.Count;
        var current = await GetVersionAsync(connection, null, cancellationToken).ConfigureAwait(false);
        if (current > target)
        {
            logger.LogError(
                "Cache schema v{Current} is newer than this app supports (v{Target}); leaving it untouched.",
                current,
                target);
            throw new LootCacheException(
                CacheFailure.SchemaTooNew,
                $"Cache schema v{current} is newer than supported v{target}.");
        }

        foreach (var migration in migrations)
        {
            if (migration.Version <= current)
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();

            // Another process may have migrated since the first read.
            current = await GetVersionAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (migration.Version <= current)
            {
                transaction.Rollback();
                continue;
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = migration.Sql
                    + $"\nPRAGMA user_version = {migration.Version.ToString(CultureInfo.InvariantCulture)};";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
            current = migration.Version;
            logger.LogInformation("Cache schema migrated to v{Version}: {Name}.", migration.Version, migration.Name);
        }
    }

    public static async Task<int> GetVersionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }
}
