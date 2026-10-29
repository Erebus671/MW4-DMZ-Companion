using DmzCompanion.Core.Logging;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Settings;

/// <summary>Persists <see cref="LogLevelSwitch.Level"/> through an <see cref="ISettingsStore"/>.</summary>
public sealed class LogLevelSettings(ISettingsStore store, LogLevelSwitch levelSwitch, ILogger<LogLevelSettings> logger)
{
    public const string Key = "logging.level";

    public AppLogLevel Current => levelSwitch.Level;

    public void Load()
    {
        string? stored;
        try
        {
            stored = store.Get(Key);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogError(ex, "Reading the saved log level failed; using {Level}.", levelSwitch.Level);
            return;
        }

        if (stored is null)
        {
            return;
        }

        if (LogLevelSwitch.TryParse(stored, out var level))
        {
            levelSwitch.Level = level;
            logger.LogDebug("Log level loaded: {Level}.", level);
        }
        else
        {
            logger.LogWarning("Saved log level {Value} is invalid; using {Level}.", stored, levelSwitch.Level);
        }
    }

    /// <summary>Applies <paramref name="level"/> immediately; returns false if it couldn't be persisted.</summary>
    public bool Save(AppLogLevel level)
    {
        levelSwitch.Level = level;
        try
        {
            store.Set(Key, level.ToString());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogError(ex, "Saving log level {Level} failed.", level);
            return false;
        }

        logger.LogInformation("Log level set to {Level}.", level);
        return true;
    }
}
