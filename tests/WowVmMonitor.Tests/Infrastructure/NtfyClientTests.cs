using System.Net;
using System.Text.Json;
using WowVmMonitor.App.Notifications;
using WowVmMonitor.Infrastructure.Notifications;

namespace WowVmMonitor.Tests.Infrastructure;

public sealed class NtfyClientTests
{
    [Fact]
    public async Task TestMessagePostsStructuredJson()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler);
        var client = new NtfyClient(httpClient, TimeSpan.FromSeconds(1), TimeSpan.Zero);

        var result = await client.SendTestAsync("wow-vm-85898-fan2354", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new Uri("https://ntfy.sh/"), handler.LastUri);
        using var document = JsonDocument.Parse(handler.LastBody);
        Assert.Equal("wow-vm-85898-fan2354", document.RootElement.GetProperty("topic").GetString());
        Assert.Equal("WowVmMonitor 测试通知", document.RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("password", handler.LastBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FirstFailureRetriesAndSucceeds()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler);
        var client = new NtfyClient(httpClient, TimeSpan.FromSeconds(1), TimeSpan.Zero);

        var result = await client.SendTestAsync("wow-vm-85898-fan2354", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task TwoFailuresReturnStableFailureCode()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable);
        using var httpClient = new HttpClient(handler);
        var client = new NtfyClient(httpClient, TimeSpan.FromSeconds(1), TimeSpan.Zero);

        var result = await client.SendTestAsync("wow-vm-85898-fan2354", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ntfy.http.503", result.FailureCode);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task TimeoutCancelsUnderlyingHttpRequest()
    {
        var handler = new HangingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new NtfyClient(httpClient, TimeSpan.FromMilliseconds(20), TimeSpan.Zero);

        var result = await client.SendTestAsync("wow-vm-85898-fan2354", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ntfy.timeout", result.FailureCode);
        Assert.True(handler.CancellationObserved);
    }

    private sealed class RecordingHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses = new(statuses);
        public int RequestCount { get; private set; }
        public Uri? LastUri { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastUri = request.RequestUri;
            LastBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_statuses.Dequeue());
        }
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        public bool CancellationObserved { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }
}
