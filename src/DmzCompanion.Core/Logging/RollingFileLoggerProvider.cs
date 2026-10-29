using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Logging;

/// <summary>Writes log entries on a background thread to daily, size-capped files.</summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly RollingFileLoggerOptions _options;
    private readonly BlockingCollection<string> _queue;
    private readonly ConcurrentDictionary<string, RollingFileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly Thread _worker;

    private StreamWriter? _writer;
    private DateOnly _fileDate;
    private int _fileIndex;
    private long _fileBytes;
    private long _dropped;
    private int _disposed;

    public RollingFileLoggerProvider(
        RollingFileLoggerOptions options,
        LogLevelSwitch levelSwitch,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(levelSwitch);
        options.Validate();

        _options = options;
        LevelSwitch = levelSwitch;
        Time = timeProvider ?? TimeProvider.System;
        _queue = new BlockingCollection<string>(options.QueueCapacity);
        _worker = new Thread(Pump) { IsBackground = true, Name = "RollingFileLogger" };
        _worker.Start();
    }

    internal LogLevelSwitch LevelSwitch { get; }

    internal TimeProvider Time { get; }

    internal string? CurrentFilePath { get; private set; }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, static (name, provider) => new RollingFileLogger(name, provider), this);

    /// <summary>Drains pending entries and closes the file; later entries are dropped.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue.CompleteAdding();
        if (!_worker.Join(_options.ShutdownTimeout))
        {
            Trace.TraceWarning("Rolling file logger did not drain within {0}.", _options.ShutdownTimeout);
        }
    }

    internal void Enqueue(string entry)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            if (!_queue.TryAdd(entry))
            {
                Interlocked.Increment(ref _dropped);
            }
        }
        catch (InvalidOperationException ex) when (_queue.IsAddingCompleted)
        {
            Debug.WriteLine($"Log entry dropped during shutdown: {ex.Message}");
        }
    }

    internal string GetFilePath(DateOnly date, int index)
    {
        var stamp = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var name = index == 0
            ? $"{_options.FilePrefix}-{stamp}.log"
            : $"{_options.FilePrefix}-{stamp}.{index.ToString(CultureInfo.InvariantCulture)}.log";
        return Path.Combine(_options.Directory, name);
    }

    private void Pump()
    {
        foreach (var entry in _queue.GetConsumingEnumerable())
        {
            var dropped = Interlocked.Exchange(ref _dropped, 0);
            if (dropped > 0)
            {
                Write(LogEntryFormatter.Format(
                    Time.GetLocalNow(),
                    LogLevel.Warning,
                    nameof(RollingFileLoggerProvider),
                    $"{dropped} log entries dropped; queue full.",
                    null));
            }

            Write(entry);
            if (_queue.Count == 0)
            {
                Flush();
            }
        }

        CloseWriter();
    }

    private void Write(string entry)
    {
        try
        {
            var writer = EnsureWriter();
            writer.Write(entry);
            _fileBytes += Utf8NoBom.GetByteCount(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceError($"Log write failed: {ex}");
            CloseWriter();
        }
    }

    private void Flush()
    {
        try
        {
            _writer?.Flush();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceError($"Log flush failed: {ex}");
            CloseWriter();
        }
    }

    private StreamWriter EnsureWriter()
    {
        var today = DateOnly.FromDateTime(Time.GetLocalNow().DateTime);
        if (_writer is not null && today == _fileDate && _fileBytes < _options.MaxFileBytes)
        {
            return _writer;
        }

        CloseWriter();
        if (today != _fileDate)
        {
            _fileDate = today;
            _fileIndex = 0;
        }

        Directory.CreateDirectory(_options.Directory);

        string path;
        long size;
        while (true)
        {
            path = GetFilePath(_fileDate, _fileIndex);
            var info = new FileInfo(path);
            size = info.Exists ? info.Length : 0;
            if (size < _options.MaxFileBytes)
            {
                break;
            }

            _fileIndex++;
        }

        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream, Utf8NoBom);
        _fileBytes = size;
        CurrentFilePath = path;
        PruneOldFiles();
        return _writer;
    }

    private void PruneOldFiles()
    {
        FileInfo[] files;
        try
        {
            files = new DirectoryInfo(_options.Directory).GetFiles($"{_options.FilePrefix}-*.log");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Log retention scan failed: {ex}");
            return;
        }

        var stale = files
            .Where(f => !string.Equals(f.FullName, CurrentFilePath, StringComparison.Ordinal))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ThenByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(_options.RetainedFileCount - 1);

        foreach (var file in stale)
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Log retention delete failed for {file.Name}: {ex}");
            }
        }
    }

    private void CloseWriter()
    {
        var writer = _writer;
        _writer = null;
        if (writer is null)
        {
            return;
        }

        try
        {
            writer.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceError($"Log file close failed: {ex}");
        }
    }
}
