using DmzCompanion.Core.Logging;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Tests;

public class LogLevelSwitchTests
{
    [Fact]
    public void DefaultsToInfo() => Assert.Equal(AppLogLevel.Info, new LogLevelSwitch().Level);

    [Theory]
    [InlineData(AppLogLevel.Debug, LogLevel.Debug)]
    [InlineData(AppLogLevel.Info, LogLevel.Information)]
    [InlineData(AppLogLevel.Warn, LogLevel.Warning)]
    [InlineData(AppLogLevel.Error, LogLevel.Error)]
    [InlineData(AppLogLevel.Critical, LogLevel.Critical)]
    [InlineData(AppLogLevel.Fatal, LogLevel.Critical)]
    public void MapsToMicrosoftLevels(AppLogLevel level, LogLevel expected) =>
        Assert.Equal(expected, LogLevelSwitch.ToLogLevel(level));

    [Fact]
    public void ChangingLevelAffectsIsEnabledImmediately()
    {
        var sw = new LogLevelSwitch(AppLogLevel.Error);
        Assert.False(sw.IsEnabled(LogLevel.Warning));

        sw.Level = AppLogLevel.Debug;

        Assert.True(sw.IsEnabled(LogLevel.Debug));
        Assert.False(sw.IsEnabled(LogLevel.Trace));
        Assert.False(sw.IsEnabled(LogLevel.None));
    }

    [Fact]
    public void ChangedFiresOnlyOnActualChange()
    {
        var sw = new LogLevelSwitch();
        var raised = new List<AppLogLevel>();
        sw.Changed += (_, level) => raised.Add(level);

        sw.Level = AppLogLevel.Info;
        sw.Level = AppLogLevel.Warn;
        sw.Level = AppLogLevel.Warn;

        Assert.Equal([AppLogLevel.Warn], raised);
    }

    [Fact]
    public void RejectsUndefinedLevel()
    {
        var sw = new LogLevelSwitch();
        Assert.Throws<ArgumentOutOfRangeException>(() => sw.Level = (AppLogLevel)42);
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogLevelSwitch((AppLogLevel)(-1)));
    }

    [Theory]
    [InlineData(AppLogLevel.Critical)]
    [InlineData(AppLogLevel.Fatal)]
    public void CriticalAndFatalPassOnlyCritical(AppLogLevel level)
    {
        var sw = new LogLevelSwitch(level);

        Assert.True(sw.IsEnabled(LogLevel.Critical));
        Assert.False(sw.IsEnabled(LogLevel.Error));
    }

    [Theory]
    [InlineData("Debug", true, AppLogLevel.Debug)]
    [InlineData(" warn ", true, AppLogLevel.Warn)]
    [InlineData("FATAL", true, AppLogLevel.Fatal)]
    [InlineData("3", false, AppLogLevel.Info)]
    [InlineData("Verbose", false, AppLogLevel.Info)]
    [InlineData("", false, AppLogLevel.Info)]
    [InlineData(null, false, AppLogLevel.Info)]
    public void TryParse(string? value, bool ok, AppLogLevel expected)
    {
        Assert.Equal(ok, LogLevelSwitch.TryParse(value, out var level));
        Assert.Equal(expected, level);
    }
}
