using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Storage;

public enum CacheOpenResult
{
    Opened,
    Recovered,
}

/// <summary>Local SQLite cache holding package data and user state. Call <see cref="InitializeAsync"/> once before use.</summary>
public sealed class CacheDatabase : IDisposable
{
    public const string FileName = "dmz-cache.db";

    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    private readonly string _connectionString;
    private readonly IReadOnlyList<SchemaMigration> _migrations;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private volatile bool _initialized;

    // Explicit init keeps provider discovery reflection-free for trimming and AOT.
    static CacheDatabase() => SQLitePCL.Batteries_V2.Init();

    public CacheDatabase(string path, ILogger<CacheDatabase> logger, TimeProvider? timeProvider = null)
        : this(path, SchemaMigrations.All, logger, timeProvider)
    {
    }

    internal CacheDatabase(string path, IReadOnlyList<SchemaMigration> migrations, ILogger logger, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(migrations);
        ArgumentNullException.ThrowIfNull(logger);

        Path = System.IO.Path.GetFullPath(path);
        _migrations = migrations;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = true,
            DefaultTimeout = 30,
        }.ToString();
    }

    public string Path { get; }

    internal TimeProvider Time => _time;

    internal int LatestSchemaVersion => _migrations.Count == 0 ? 0 : _migrations[^1].Version;

    /// <summary>Creates or upgrades the database. A corrupt file is moved aside and rebuilt.</summary>
    /// <exception cref="LootCacheException">The file can't be used.</exception>
    public async Task<CacheOpenResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var result = CacheOpenResult.Opened;
            try
            {
                await OpenAndMigrateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteCorrupt or SqliteNotADatabase)
            {
                _logger.LogError(ex, "Cache database {Path} is corrupt; moving it aside and rebuilding.", Path);
                ClearPool();
                Quarantine();
                await OpenAndMigrateAsync(cancellationToken).ConfigureAwait(false);
                result = CacheOpenResult.Recovered;
            }

            _initialized = true;
            return result;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Opening cache database {Path} failed.", Path);
            throw new LootCacheException(CacheFailure.Storage, $"Opening cache database '{Path}' failed.", ex);
        }
    }

    public void Dispose() => ClearPool();

    internal async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Cache database isn't initialized.");
        }

        return await OpenRawAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="work"/> on a fresh connection; storage failures are logged and wrapped.</summary>
    internal async Task<T> RunAsync<T>(
        string operation,
        Func<SqliteConnection, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                return await work(connection, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.LogError(ex, "Cache operation {Operation} failed on {Path}.", operation, Path);
            throw new LootCacheException(CacheFailure.Storage, $"Cache operation '{operation}' failed.", ex);
        }
    }

    private async Task<SqliteConnection> OpenRawAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task OpenAndMigrateAsync(CancellationToken cancellationToken)
    {
        var connection = await OpenRawAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using (var wal = connection.CreateCommand())
            {
                wal.CommandText = "PRAGMA journal_mode = WAL;";
                await wal.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }

            await SchemaMigrator.MigrateAsync(connection, _migrations, _logger, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Quarantine()
    {
        var stamp = _time.GetUtcNow().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        foreach (var suffix in (string[])["", "-wal", "-shm"])
        {
            var source = Path + suffix;
            if (File.Exists(source))
            {
                File.Move(source, $"{Path}.corrupt-{stamp}{suffix}");
            }
        }

        _logger.LogWarning("Corrupt cache moved to {Path}.corrupt-{Stamp}.", Path, stamp);
    }

    private void ClearPool()
    {
        using var connection = new SqliteConnection(_connectionString);
        SqliteConnection.ClearPool(connection);
    }
}
