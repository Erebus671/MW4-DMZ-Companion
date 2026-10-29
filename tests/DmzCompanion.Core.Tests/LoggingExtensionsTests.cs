using DmzCompanion.Core.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Tests;

public class LoggingExtensionsTests
{
    [Fact]
    public void LevelSwitchGatesExistingLoggersAtRuntime()
    {
        var sw = new LogLevelSwitch(AppLogLevel.Warn);
        var capture = new CaptureLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddLevelSwitch(sw).AddProvider(capture));
        var logger = factory.CreateLogger("Test");

        logger.LogInformation("hidden");
        sw.Level = AppLogLevel.Debug;
        logger.LogDebug("shown");

        Assert.Equal(["shown"], capture.Records.Select(r => r.Message));
    }

    [Fact]
    public void LogFatalUsesCriticalWithFatalEvent()
    {
        var capture = new CaptureLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddLevelSwitch(new LogLevelSwitch()).AddProvider(capture));

        factory.CreateLogger("Test").LogFatal(null, "boom {Code}", 7);

        var record = Assert.Single(capture.Records);
        Assert.Equal(LogLevel.Critical, record.Level);
        Assert.True(LogLevelSwitch.IsFatal(record.EventId));
        Assert.Equal("boom 7", record.Message);
    }

    [Fact]
    public void AddRollingFileRegistersSingleProvider()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dmz-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var services = new ServiceCollection();
            var sw = new LogLevelSwitch();
            services.AddLogging(b => b.AddLevelSwitch(sw)
                .AddRollingFile(new RollingFileLoggerOptions { Directory = dir }, sw)
                .AddRollingFile(new RollingFileLoggerOptions { Directory = dir }, sw));

            using var sp = services.BuildServiceProvider();
            var providers = sp.GetServices<ILoggerProvider>().OfType<RollingFileLoggerProvider>().ToList();

            Assert.Single(providers);
            Assert.Same(providers[0], sp.GetRequiredService<RollingFileLoggerProvider>());
        }
        finally
        {
            TestFiles.DeleteDirectory(dir);
        }
    }
}
