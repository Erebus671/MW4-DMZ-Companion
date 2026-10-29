namespace DmzCompanion.Core.Storage;

public enum CacheFailure
{
    Storage,
    SchemaTooNew,
}

/// <summary>Cache operation failed. Already logged by the cache; callers shouldn't log it again.</summary>
public sealed class LootCacheException : Exception
{
    public LootCacheException()
    {
    }

    public LootCacheException(string message)
        : base(message)
    {
    }

    public LootCacheException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public LootCacheException(CacheFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public CacheFailure Failure { get; }
}

/// <summary>Package failed validation and wasn't cached.</summary>
public sealed class InvalidLootPackageException : Exception
{
    public InvalidLootPackageException()
    {
    }

    public InvalidLootPackageException(string message)
        : base(message)
    {
    }

    public InvalidLootPackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public InvalidLootPackageException(IReadOnlyList<string> errors)
        : base($"Loot package is invalid ({errors.Count} problems): {string.Join("; ", errors.Take(5))}")
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; } = [];
}
