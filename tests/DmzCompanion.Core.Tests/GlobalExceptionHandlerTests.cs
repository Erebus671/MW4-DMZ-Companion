using DmzCompanion.Core.Diagnostics;
using DmzCompanion.Core.Logging;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Tests;

public class GlobalExceptionHandlerTests
{
    private readonly CaptureLogger<GlobalExceptionHandler> _logger = new();

    [Fact]
    public void TerminatingLogsFatalAndRunsCallback()
    {
        var terminated = false;
        var handler = new GlobalExceptionHandler(_logger, () => terminated = true);

        handler.HandleUnhandled(new InvalidOperationException("x"), isTerminating: true, "Test");

        var record = Assert.Single(_logger.Records);
        Assert.Equal(LogLevel.Critical, record.Level);
        Assert.True(LogLevelSwitch.IsFatal(record.EventId));
        Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.True(terminated);
    }

    [Fact]
    public void NonTerminatingLogsCritical()
    {
        new GlobalExceptionHandler(_logger).HandleUnhandled(new InvalidOperationException(), isTerminating: false, "Test");

        var record = Assert.Single(_logger.Records);
        Assert.Equal(LogLevel.Critical, record.Level);
        Assert.False(LogLevelSwitch.IsFatal(record.EventId));
    }

    [Fact]
    public void NonExceptionObjectIsWrapped()
    {
        new GlobalExceptionHandler(_logger).HandleUnhandled("oops", isTerminating: false, "Test");

        var record = Assert.Single(_logger.Records);
        Assert.Contains("oops", record.Exception?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnobservedLogsCritical()
    {
        new GlobalExceptionHandler(_logger).HandleUnobserved(new AggregateException());

        Assert.Equal(LogLevel.Critical, Assert.Single(_logger.Records).Level);
    }
}
