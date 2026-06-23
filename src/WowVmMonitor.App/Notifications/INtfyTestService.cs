namespace WowVmMonitor.App.Notifications;

public interface INtfyTestService
{
    Task<NtfySendResult> SendTestAsync(string topic, CancellationToken cancellationToken);
}
