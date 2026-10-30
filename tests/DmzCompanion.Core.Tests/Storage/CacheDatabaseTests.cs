using DmzCompanion.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Tests.Storage;

public sealed class CacheDatabaseTests : CacheTestBase
{
    [Fact]
    public async Task FreshDatabase_MigratesToLatest()
    {
        using var db = NewDatabase();

        var result = await db.InitializeAsync(Ct);

        Assert.Equal(CacheOpenResult.Opened, result);
        Assert.Equal(SchemaMigrations.All.Count, await ReadVersionAsync(Ct));
        Assert.Contains(DbLogger.Records, r => r.Level == LogLevel.Information && r.Message.Contains("v1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reopen_IsIdempotent()
    {
        using (var first = await OpenAsync(Ct))
        {
            await new UserStateStore(first).SetCashAsync(42, Ct);
        }

        using var second = NewDatabase();
        await second.InitializeAsync(Ct);

        Assert.Equal(42, await new UserStateStore(second).GetCashAsync(Ct));
        Assert.Equal(SchemaMigrations.All.Count, DbLogger.Records.Count(r => r.Level == LogLevel.Information));
    }

    [Fact]
    public async Task V1Database_UpgradesWithRecipesVerified()
    {
        using (var old = NewDatabase(SchemaMigrations.All.Take(1).ToArray()))
        {
            await old.InitializeAsync(Ct);
        }

        using var updated = NewDatabase();
        await updated.InitializeAsync(Ct);

        Assert.Equal(SchemaMigrations.All.Count, await ReadVersionAsync(Ct));
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('recipes') WHERE name = 'unverified' AND dflt_value = '0';", Ct));
    }

    [Fact]
    public async Task Upgrade_AppliesOnlyNewStepsAndKeepsData()
    {
        SchemaMigration[] v1 = [new(1, "one", "CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL) STRICT; INSERT INTO t VALUES (1, 'kept');")];
        SchemaMigration[] v2 = [v1[0], new(2, "two", "ALTER TABLE t ADD COLUMN extra TEXT NOT NULL DEFAULT 'x';")];

        using (var old = NewDatabase(v1))
        {
            await old.InitializeAsync(Ct);
        }

        using var updated = NewDatabase(v2);
        await updated.InitializeAsync(Ct);

        Assert.Equal(2, await ReadVersionAsync(Ct));
        Assert.Equal("kept|x", await ScalarAsync("SELECT name || '|' || extra FROM t WHERE id = 1;", Ct));
    }

    [Fact]
    public async Task FailedMigration_RollsBackAndThrows()
    {
        SchemaMigration[] v1 = [new(1, "one", "CREATE TABLE t (id INTEGER PRIMARY KEY) STRICT;")];
        SchemaMigration[] broken = [v1[0], new(2, "broken", "CREATE TABLE half (id INTEGER) STRICT; THIS IS NOT SQL;")];

        using (var old = NewDatabase(v1))
        {
            await old.InitializeAsync(Ct);
        }

        using var db = NewDatabase(broken);
        var ex = await Assert.ThrowsAsync<LootCacheException>(() => db.InitializeAsync(Ct));

        Assert.Equal(CacheFailure.Storage, ex.Failure);
        Assert.Equal(1, await ReadVersionAsync(Ct));
        Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name = 'half';", Ct));
        Assert.Contains(DbLogger.Records, r => r.Level == LogLevel.Error);
    }

    [Fact]
    public async Task NewerSchema_IsLeftUntouched()
    {
        using (var db = await OpenAsync(Ct))
        {
            await new UserStateStore(db).SetCashAsync(7, Ct);
        }

        await ExecAsync("PRAGMA user_version = 99;", Ct);

        using var older = NewDatabase();
        var ex = await Assert.ThrowsAsync<LootCacheException>(() => older.InitializeAsync(Ct));

        Assert.Equal(CacheFailure.SchemaTooNew, ex.Failure);
        Assert.Equal(99, await ReadVersionAsync(Ct));
        Assert.Equal(7L, await ScalarAsync("SELECT value FROM user_values WHERE key = 'cash';", Ct));
    }

    [Fact]
    public async Task CorruptFile_IsQuarantinedAndRebuilt()
    {
        System.IO.Directory.CreateDirectory(Directory);
        await File.WriteAllTextAsync(DbPath, new string('x', 8192), Ct);

        using var db = NewDatabase();
        var result = await db.InitializeAsync(Ct);

        Assert.Equal(CacheOpenResult.Recovered, result);
        Assert.Single(System.IO.Directory.GetFiles(Directory, CacheDatabase.FileName + ".corrupt-*"));
        Assert.Equal(SchemaMigrations.All.Count, await ReadVersionAsync(Ct));
        Assert.Contains(DbLogger.Records, r => r.Level == LogLevel.Error && r.Message.Contains("corrupt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnusablePath_ThrowsStorageFailureAndLogsError()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var blocker = Path.Combine(Directory, "blocker");
        await File.WriteAllTextAsync(blocker, "file", Ct);

        using var db = new CacheDatabase(Path.Combine(blocker, CacheDatabase.FileName), SchemaMigrations.All, DbLogger, Time);
        var ex = await Assert.ThrowsAsync<LootCacheException>(() => db.InitializeAsync(Ct));

        Assert.Equal(CacheFailure.Storage, ex.Failure);
        Assert.Contains(DbLogger.Records, r => r.Level == LogLevel.Error && r.Exception is not null);
    }

    [Fact]
    public async Task UseBeforeInitialize_Throws()
    {
        using var db = NewDatabase();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new UserStateStore(db).GetCashAsync(Ct));
    }

    [Fact]
    public async Task OutOfOrderMigrations_AreAProgrammingError()
    {
        SchemaMigration[] gap = [new(2, "two", "SELECT 1;")];
        using var db = NewDatabase(gap);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.InitializeAsync(Ct));
    }

    private async Task<long> ReadVersionAsync(CancellationToken cancellationToken) =>
        (long)(await ScalarAsync("PRAGMA user_version;", cancellationToken))!;

    private async Task<object?> ScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private async Task ExecAsync(string sql, CancellationToken cancellationToken)
    {
        SqliteConnection.ClearAllPools();
        await using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
