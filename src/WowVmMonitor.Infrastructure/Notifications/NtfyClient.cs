using System.Net.Http.Json;
using WowVmMonitor.App.Notifications;
using WowVmMonitor.App.Presentation;

namespace WowVmMonitor.Infrastructure.Notifications;

public sealed class NtfyClient : INtfySender, INtfyTestService
{
    private static readonly Uri ServerUri = new("https://ntfy.sh/");
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _retryDelay;

    public NtfyClient(HttpClient httpClient, TimeSpan timeout, TimeSpan retryDelay)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        if (retryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        }

        _httpClient = httpClient;
        _timeout = timeout;
        _retryDelay = retryDelay;
    }

    public Task<NtfySendResult> SendTestAsync(string topic, CancellationToken cancellationToken) =>
        SendPayloadAsync(new NtfyPayload(
            topic,
            "WowVmMonitor 测试通知",
            "WowVmMonitor ntfy 连接测试成功。",
            3,
            ["white_check_mark"]), cancellationToken);

    public Task<NtfySendResult> SendAsync(
        NtfyNotification notification,
        string topic,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var isAlert = notification.Kind == NtfyNotificationKind.Alert;
        var age = notification.LogAge is null
            ? string.Empty
            : $"\n日志年龄：{notification.LogAge.Value:hh\\:mm\\:ss}";
        var message = $"机器：{notification.MachineDisplayName}\n状态：{(isAlert ? "异常" : "已恢复")}\n时间：{LocalTimeText.Format(notification.OccurredAt)}{age}";
        return SendPayloadAsync(new NtfyPayload(
            topic,
            isAlert ? "WowVmMonitor 异常告警" : "WowVmMonitor 恢复正常",
            message,
            isAlert ? 5 : 3,
            [isAlert ? "warning" : "white_check_mark"]), cancellationToken);
    }

    private async Task<NtfySendResult> SendPayloadAsync(
        NtfyPayload payload,
        CancellationToken cancellationToken)
    {
        NtfySendResult result = NtfySendResult.Failure("ntfy.unknown");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await _httpClient
                    .PostAsJsonAsync(ServerUri, payload, cancellationToken)
                    .WaitAsync(_timeout, cancellationToken)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return NtfySendResult.Success();
                }

                result = NtfySendResult.Failure($"ntfy.http.{(int)response.StatusCode}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException)
            {
                result = NtfySendResult.Failure("ntfy.timeout");
            }
            catch (HttpRequestException)
            {
                result = NtfySendResult.Failure("ntfy.network");
            }

            if (attempt == 0 && _retryDelay > TimeSpan.Zero)
            {
                await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        return result;
    }

    private sealed record NtfyPayload(
        string Topic,
        string Title,
        string Message,
        int Priority,
        IReadOnlyList<string> Tags);
}
