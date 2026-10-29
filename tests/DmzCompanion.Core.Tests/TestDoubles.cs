using System.Collections.Concurrent;
using DmzCompanion.Core.Settings;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Tests;

internal sealed record LogRecord(string Category, LogLevel Level, EventId EventId, string Message, Exception? Exception);

internal sealed class CaptureLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<LogRecord> Records { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, Records);

    public void Dispose()
    {
    }
}

internal class CaptureLogger(string category, ConcurrentQueue<LogRecord> records) : ILogger
{
    public ConcurrentQueue<LogRecord> Records => records;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        records.Enqueue(new LogRecord(category, logLevel, eventId, formatter(state, exception), exception));
}

internal sealed class CaptureLogger<T>() : CaptureLogger(typeof(T).Name, new ConcurrentQueue<LogRecord>()), ILogger<T>;

internal sealed class MemorySettingsStore : ISettingsStore
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public Exception? GetFailure { get; set; }

    public Exception? SetFailure { get; set; }

    public string? Get(string key)
    {
        if (GetFailure is not null)
        {
            throw GetFailure;
        }

        return Values.GetValueOrDefault(key);
    }

    public void Set(string key, string value)
    {
        if (SetFailure is not null)
        {
            throw SetFailure;
        }

        Values[key] = value;
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => Now;
}
