using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Logging;

internal sealed class RollingFileLogger(string category, RollingFileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => provider.LevelSwitch.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (!provider.LevelSwitch.IsEnabled(logLevel))
        {
            return;
        }

        var entry = LogEntryFormatter.Format(
            provider.Time.GetLocalNow(),
            logLevel,
            category,
            formatter(state, exception),
            exception);
        provider.Enqueue(entry);
    }
}
