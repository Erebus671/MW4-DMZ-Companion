using System.Net;
using DmzCompanion.Core.Packaging;

namespace DmzCompanion.Core.Tests.Packaging;

public sealed class HttpPackageDownloaderTests
{
    private static readonly Uri Source = new("https://packages.example/loot-package-r1.json");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Download_ReturnsBody()
    {
        var downloader = Create(_ => Ok(new ByteArrayContent([1, 2, 3])));

        Assert.Equal([1, 2, 3], await downloader.DownloadAsync(Source, 3, Ct));
    }

    [Fact]
    public async Task DeclaredLengthOverCap_IsRejected()
    {
        var downloader = Create(_ => Ok(new ByteArrayContent(new byte[11])));

        await AssertFailsAsync(downloader, DownloadFailure.TooLarge, 10);
    }

    [Fact]
    public async Task UndeclaredLengthOverCap_IsRejectedWhileStreaming()
    {
        var downloader = Create(_ => Ok(new UnknownLengthContent(new byte[200_000])));

        await AssertFailsAsync(downloader, DownloadFailure.TooLarge, 100_000);
    }

    [Fact]
    public async Task HttpUrl_IsRejectedWithoutRequest()
    {
        var requested = false;
        var downloader = Create(_ =>
        {
            requested = true;
            return Ok(new ByteArrayContent([]));
        });

        var ex = await Assert.ThrowsAsync<PackageDownloadException>(
            () => downloader.DownloadAsync(new Uri("http://packages.example/p.json"), 10, Ct));

        Assert.Equal(DownloadFailure.NotHttps, ex.Failure);
        Assert.False(requested);
    }

    [Fact]
    public async Task RedirectToHttp_IsRejected()
    {
        var downloader = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1]),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://mirror.example/p.json"),
        });

        await AssertFailsAsync(downloader, DownloadFailure.NotHttps, 10);
    }

    [Fact]
    public async Task ErrorStatus_IsReported()
    {
        var downloader = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await AssertFailsAsync(downloader, DownloadFailure.HttpStatus, 10);
    }

    [Fact]
    public async Task ConnectionFailure_IsNetworkFailure()
    {
        var downloader = Create(_ => throw new HttpRequestException("refused"));

        await AssertFailsAsync(downloader, DownloadFailure.Network, 10);
    }

    [Fact]
    public async Task Timeout_IsNetworkFailure()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Ok(new ByteArrayContent([]));
        });
        using var client = new HttpClient(handler);
        var downloader = new HttpPackageDownloader(client, TimeSpan.FromMilliseconds(50));

        await AssertFailsAsync(downloader, DownloadFailure.Network, 10);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var handler = new StubHandler(async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Ok(new ByteArrayContent([]));
        });
        using var client = new HttpClient(handler);
        var downloader = new HttpPackageDownloader(client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloader.DownloadAsync(Source, 10, cts.Token));
    }

    [Fact]
    public void CreateClient_LeavesTimeoutsToDownloader()
    {
        using var client = HttpPackageDownloader.CreateClient();

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
        Assert.NotEmpty(client.DefaultRequestHeaders.UserAgent);
    }

    private static HttpPackageDownloader Create(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler((request, _) => Task.FromResult(respond(request)))));

    private static HttpResponseMessage Ok(HttpContent content) => new(HttpStatusCode.OK) { Content = content };

    private static async Task AssertFailsAsync(HttpPackageDownloader downloader, DownloadFailure expected, long maxBytes)
    {
        var ex = await Assert.ThrowsAsync<PackageDownloadException>(() => downloader.DownloadAsync(Source, maxBytes, Ct));
        Assert.Equal(expected, ex.Failure);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await respond(request, cancellationToken);
            response.RequestMessage ??= request;
            return response;
        }
    }

    private sealed class UnknownLengthContent(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(body).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
