using WowVmMonitor.Core.Configuration;

namespace WowVmMonitor.App.Notifications;

public sealed class NullNotificationDispatcher : INotificationDispatcher
{
    public void Configure(NtfyConfiguration configuration) { }

    public bool TryEnqueue(NtfyNotification notification) => false;
}
