using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Logging;

public static class LoggingExtensions
{
    /// <summary>Gates every provider on <paramref name="levelSwitch"/>, evaluated per call.</summary>
    public static ILoggingBuilder AddLevelSwitch(this ILoggingBuilder builder, LogLevelSwitch levelSwitch)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(levelSwitch);

        builder.Services.TryAddSingleton(levelSwitch);
        builder.SetMinimumLevel(LogLevel.Trace);
        builder.AddFilter((_, _, level) => levelSwitch.IsEnabled(level));
        return builder;
    }

    public static ILoggingBuilder AddRollingFile(
        this ILoggingBuilder builder,
        RollingFileLoggerOptions options,
        LogLevelSwitch levelSwitch)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(levelSwitch);

        builder.Services.TryAddSingleton(_ => new RollingFileLoggerProvider(options, levelSwitch));
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, RollingFileLoggerProvider>(
                sp => sp.GetRequiredService<RollingFileLoggerProvider>()));
        return builder;
    }

    [SuppressMessage("Usage", "CA2254", Justification = "Pass-through; callers supply constant templates.")]
    public static void LogFatal(this ILogger logger, Exception? exception, string message, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        logger.Log(LogLevel.Critical, LogLevelSwitch.FatalEvent, exception, message, args);
    }
}
