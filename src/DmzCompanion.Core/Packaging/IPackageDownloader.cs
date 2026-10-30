namespace DmzCompanion.Core.Packaging;

/// <summary>Fetches package files. Swappable for tests.</summary>
public interface IPackageDownloader
{
    /// <exception cref="PackageDownloadException">The download failed or exceeded <paramref name="maxBytes"/>.</exception>
    Task<byte[]> DownloadAsync(Uri uri, long maxBytes, CancellationToken ct);
}

public enum DownloadFailure
{
    Network,
    TooLarge,
    NotHttps,
    HttpStatus,
}

/// <summary>Expected download failure. Callers keep the installed package.</summary>
public sealed class PackageDownloadException : Exception
{
    public PackageDownloadException()
    {
    }

    public PackageDownloadException(string message)
        : base(message)
    {
    }

    public PackageDownloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public PackageDownloadException(DownloadFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public DownloadFailure Failure { get; }
}
