using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Logging;

/// <summary>Runtime-switchable minimum log level.</summary>
/// <remarks>Fatal and Critical both map to LogLevel.Critical.</remarks>
public sealed class LogLevelSwitch
{
    public const AppLogLevel DefaultLevel = AppLogLevel.Info;

    private volatile AppLogLevel _level;

    public LogLevelSwitch(AppLogLevel initialLevel = DefaultLevel)
    {
        EnsureDefined(initialLevel);
        _level = initialLevel;
    }

    public event EventHandler<AppLogLevel>? Changed;

    public AppLogLevel Level
    {
        get => _level;
        set
        {
            EnsureDefined(value);
            if (_level == value)
            {
                return;
            }

            _level = value;
            Changed?.Invoke(this, value);
        }
    }

    public LogLevel MinimumLevel => ToLogLevel(_level);

    public bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= MinimumLevel;

    public static LogLevel ToLogLevel(AppLogLevel level) => level switch
    {
        AppLogLevel.Debug => LogLevel.Debug,
        AppLogLevel.Info => LogLevel.Information,
        AppLogLevel.Warn => LogLevel.Warning,
        AppLogLevel.Error => LogLevel.Error,
        AppLogLevel.Critical or AppLogLevel.Fatal => LogLevel.Critical,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    public static bool TryParse(string? value, out AppLogLevel level)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && !int.TryParse(value, out _)
            && Enum.TryParse(value.Trim(), ignoreCase: true, out level)
            && Enum.IsDefined(level))
        {
            return true;
        }

        level = DefaultLevel;
        return false;
    }

    private static void EnsureDefined(AppLogLevel level)
    {
        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, null);
        }
    }
}
