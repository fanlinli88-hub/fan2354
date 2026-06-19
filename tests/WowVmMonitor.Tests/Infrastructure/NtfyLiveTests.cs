using WowVmMonitor.Infrastructure.Notifications;

namespace WowVmMonitor.Tests.Infrastructure;

public sealed class NtfyLiveTests
{
    [Fact]
    public async Task SendsAuthorizedLiveTestMessage()
    {
        var topic = Environment.GetEnvironmentVariable("WOWVMMONITOR_NTFY_LIVE_TOPIC");
        if (string.IsNullOrWhiteSpace(topic))
        {
            return;
        }

        using var httpClient = new HttpClient();
        var client = new NtfyClient(
            httpClient,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(1));

        var result = await client.SendTestAsync(topic, CancellationToken.None);

        Assert.True(result.Succeeded, result.FailureCode);
    }
}
