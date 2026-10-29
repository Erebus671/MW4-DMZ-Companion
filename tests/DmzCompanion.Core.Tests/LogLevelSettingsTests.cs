using DmzCompanion.Core.Logging;
using DmzCompanion.Core.Settings;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Tests;

public class LogLevelSettingsTests
{
    private readonly MemorySettingsStore _store = new();
    private readonly LogLevelSwitch _switch = new();
    private readonly CaptureLogger<LogLevelSettings> _logger = new();

    private LogLevelSettings Settings => new(_store, _switch, _logger);

    [Fact]
    public void LoadAppliesStoredLevel()
    {
        _store.Values[LogLevelSettings.Key] = "Error";

        Settings.Load();

        Assert.Equal(AppLogLevel.Error, _switch.Level);
        Assert.DoesNotContain(_logger.Records, r => r.Level >= LogLevel.Warning);
    }

    [Fact]
    public void LoadWithNothingStoredKeepsDefaultSilently()
    {
        Settings.Load();

        Assert.Equal(AppLogLevel.Info, _switch.Level);
        Assert.Empty(_logger.Records);
    }

    [Fact]
    public void LoadWithInvalidValueWarns()
    {
        _store.Values[LogLevelSettings.Key] = "Loud";

        Settings.Load();

        Assert.Equal(AppLogLevel.Info, _switch.Level);
        Assert.Contains(_logger.Records, r => r.Level == LogLevel.Warning);
    }

    [Fact]
    public void LoadStoreFailureLogsError()
    {
        _store.GetFailure = new IOException("disk");

        Settings.Load();

        Assert.Equal(AppLogLevel.Info, _switch.Level);
        Assert.Contains(_logger.Records, r => r.Level == LogLevel.Error && r.Exception is IOException);
    }

    [Fact]
    public void SavePersistsAndApplies()
    {
        Assert.True(Settings.Save(AppLogLevel.Debug));

        Assert.Equal(AppLogLevel.Debug, _switch.Level);
        Assert.Equal("Debug", _store.Values[LogLevelSettings.Key]);
    }

    [Fact]
    public void SaveFailureAppliesButReportsFalse()
    {
        _store.SetFailure = new UnauthorizedAccessException();

        Assert.False(Settings.Save(AppLogLevel.Warn));

        Assert.Equal(AppLogLevel.Warn, _switch.Level);
        Assert.Contains(_logger.Records, r => r.Level == LogLevel.Error);
    }
}
