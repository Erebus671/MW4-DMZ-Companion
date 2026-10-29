namespace DmzCompanion.Core.Logging;

public sealed class RollingFileLoggerOptions
{
    public required string Directory { get; init; }

    public string FilePrefix { get; init; } = "dmz";

    public long MaxFileBytes { get; init; } = 5 * 1024 * 1024;

    public int RetainedFileCount { get; init; } = 10;

    public int QueueCapacity { get; init; } = 10_000;

    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(2);

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(FilePrefix);
        if (FilePrefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Invalid file name characters.", nameof(FilePrefix));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(MaxFileBytes, 1024);
        ArgumentOutOfRangeException.ThrowIfLessThan(RetainedFileCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(QueueCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(ShutdownTimeout, TimeSpan.Zero);
    }
}
