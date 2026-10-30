using System.Net;
using System.Net.Http.Headers;

namespace DmzCompanion.Core.Packaging;

/// <summary>HTTPS-only downloads with a per-request timeout and a size cap enforced while streaming.</summary>
public sealed class HttpPackageDownloader : IPackageDownloader
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private const int BufferSize = 81920;

    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;

    public HttpPackageDownloader(HttpClient client, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _timeout = timeout ?? DefaultTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_timeout, TimeSpan.Zero, nameof(timeout));
    }

    /// <summary>Client for downloads: limited redirects, no cookies, timeouts handled per request.</summary>
    public static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DMZCompanion", "1.0"));
        return client;
    }

    public async Task<byte[]> DownloadAsync(Uri uri, long maxBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);
        RequireHttps(uri);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            RequireHttps(response.RequestMessage?.RequestUri ?? uri);
            if (!response.IsSuccessStatusCode)
            {
                throw new PackageDownloadException(
                    DownloadFailure.HttpStatus, $"{uri} returned HTTP {(int)response.StatusCode}.");
            }

            if (response.Content.Headers.ContentLength is { } length && length > maxBytes)
            {
                throw TooLarge(uri, maxBytes);
            }

            var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                return await ReadCappedAsync(stream, uri, maxBytes, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new PackageDownloadException(DownloadFailure.Network, $"{uri} timed out after {_timeout}.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new PackageDownloadException(DownloadFailure.Network, $"{uri} couldn't be downloaded: {ex.Message}", ex);
        }
    }

    private static async Task<byte[]> ReadCappedAsync(Stream stream, Uri uri, long maxBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[BufferSize];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw TooLarge(uri, maxBytes);
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static void RequireHttps(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new PackageDownloadException(DownloadFailure.NotHttps, $"{uri} isn't an https URL.");
        }
    }

    private static PackageDownloadException TooLarge(Uri uri, long maxBytes) =>
        new(DownloadFailure.TooLarge, $"{uri} is larger than {maxBytes} bytes.");
}
