using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Diagnostics;

/// <summary>Logs exceptions nothing else caught.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, Action? onTerminating = null)
{
    private int _registered;

    public void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public void HandleUnhandled(object? exceptionObject, bool isTerminating, string source)
    {
        var exception = exceptionObject as Exception
            ?? new InvalidOperationException($"Non-exception object thrown: {exceptionObject}");

        if (isTerminating)
        {
            logger.LogCritical(exception, "Unhandled exception from {Source}; terminating.", source);
            onTerminating?.Invoke();
        }
        else
        {
            logger.LogCritical(exception, "Unhandled exception from {Source}.", source);
        }
    }

    public void HandleUnobserved(Exception exception) =>
        logger.LogCritical(exception, "Unobserved task exception.");

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e) =>
        HandleUnhandled(e.ExceptionObject, e.IsTerminating, "AppDomain");

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        HandleUnobserved(e.Exception);
        e.SetObserved();
    }
}
