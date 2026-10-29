using DmzCompanion.Core.Logging;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Tests;

public sealed class RollingFileLoggerProviderTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dmz-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new(Start);

    public void Dispose() => TestFiles.DeleteDirectory(_dir);

    [Fact]
    public void WritesFormattedEntries()
    {
        var sw = new LogLevelSwitch(AppLogLevel.Debug);
        using (var provider = Create(sw))
        {
            var logger = provider.CreateLogger("Cat");
            logger.LogDebug("hello {Name}", "world");
            logger.LogError(new InvalidOperationException("bad"), "failed");
        }

        var text = File.ReadAllText(Path.Combine(_dir, "dmz-20261029.log"));
        Assert.Contains("2026-10-29 12:00:00.000 +00:00 DBG Cat: hello world", text, StringComparison.Ordinal);
        Assert.Contains("ERR Cat: failed", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: bad", text, StringComparison.Ordinal);
    }

    [Fact]
    public void HonorsRuntimeLevelChanges()
    {
        var sw = new LogLevelSwitch(AppLogLevel.Error);
        using (var provider = Create(sw))
        {
            var logger = provider.CreateLogger("Cat");
            logger.LogWarning("dropped");
            sw.Level = AppLogLevel.Warn;
            logger.LogWarning("kept");
        }

        var text = ReadAll();
        Assert.DoesNotContain("dropped", text, StringComparison.Ordinal);
        Assert.Contains("WRN Cat: kept", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FatalLevelWritesOnlyFatalEntries()
    {
        var sw = new LogLevelSwitch(AppLogLevel.Fatal);
        using (var provider = Create(sw))
        {
            var logger = provider.CreateLogger("Cat");
            logger.LogCritical("critical");
            logger.LogFatal(null, "fatal");
        }

        var text = ReadAll();
        Assert.DoesNotContain("critical", text, StringComparison.Ordinal);
        Assert.Contains("FTL Cat: fatal", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RollsBySizeAndDate()
    {
        var sw = new LogLevelSwitch();
        using (var provider = Create(sw, maxBytes: 1024))
        {
            var logger = provider.CreateLogger("Cat");
            var payload = new string('x', 600);
            logger.LogInformation("{Payload}", payload);
            logger.LogInformation("{Payload}", payload);
            logger.LogInformation("{Payload}", payload);
            WaitFor(() => File.Exists(Path.Combine(_dir, "dmz-20261029.1.log")));
            _time.Now = Start.AddDays(1);
            logger.LogInformation("next day");
        }

        Assert.True(File.Exists(Path.Combine(_dir, "dmz-20261029.log")));
        Assert.True(File.Exists(Path.Combine(_dir, "dmz-20261029.1.log")));
        Assert.Contains("next day", File.ReadAllText(Path.Combine(_dir, "dmz-20261030.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void PrunesBeyondRetention()
    {
        Directory.CreateDirectory(_dir);
        for (var i = 1; i <= 5; i++)
        {
            var path = Path.Combine(_dir, $"dmz-202610{i:00}.log");
            File.WriteAllText(path, "old");
            File.SetLastWriteTimeUtc(path, Start.UtcDateTime.AddDays(-30 + i));
        }

        File.WriteAllText(Path.Combine(_dir, "other.log"), "keep");

        using (var provider = Create(new LogLevelSwitch(), retained: 3))
        {
            provider.CreateLogger("Cat").LogInformation("now");
        }

        var remaining = Directory.GetFiles(_dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["dmz-20261004.log", "dmz-20261005.log", "dmz-20261029.log", "other.log"], remaining);
    }

    [Fact]
    public void EntriesAfterDisposeAreIgnored()
    {
        var provider = Create(new LogLevelSwitch());
        var logger = provider.CreateLogger("Cat");
        provider.Dispose();
        provider.Dispose();

        logger.LogError("late");

        Assert.False(Directory.Exists(_dir) && ReadAll().Contains("late", StringComparison.Ordinal));
    }

    [Fact]
    public void UnwritableDirectoryDoesNotThrow()
    {
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, string.Empty);
        var options = new RollingFileLoggerOptions { Directory = Path.Combine(blocker, "logs") };

        using var provider = new RollingFileLoggerProvider(options, new LogLevelSwitch(), _time);
        provider.CreateLogger("Cat").LogError("lost");
    }

    [Theory]
    [InlineData("", 1024, 1)]
    [InlineData("dir", 10, 1)]
    [InlineData("dir", 1024, 0)]
    public void RejectsInvalidOptions(string dir, long maxBytes, int retained)
    {
        var options = new RollingFileLoggerOptions { Directory = dir, MaxFileBytes = maxBytes, RetainedFileCount = retained };
        Assert.ThrowsAny<ArgumentException>(() => new RollingFileLoggerProvider(options, new LogLevelSwitch()));
    }

    private RollingFileLoggerProvider Create(LogLevelSwitch sw, long maxBytes = 1024 * 1024, int retained = 10) =>
        new(new RollingFileLoggerOptions { Directory = _dir, MaxFileBytes = maxBytes, RetainedFileCount = retained }, sw, _time);

    private string ReadAll() =>
        string.Concat(Directory.GetFiles(_dir, "*.log").Order(StringComparer.Ordinal).Select(File.ReadAllText));

    private static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for log writer.");
            Thread.Sleep(10);
        }
    }
}
