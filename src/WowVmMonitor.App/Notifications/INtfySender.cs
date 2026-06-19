namespace WowVmMonitor.App.Notifications;

public interface INtfySender
{
    Task<NtfySendResult> SendAsync(
        NtfyNotification notification,
        string topic,
        CancellationToken cancellationToken);
}
