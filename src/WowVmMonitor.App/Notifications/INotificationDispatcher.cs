using WowVmMonitor.Core.Configuration;

namespace WowVmMonitor.App.Notifications;

public interface INotificationDispatcher
{
    void Configure(NtfyConfiguration configuration);

    bool TryEnqueue(NtfyNotification notification);
}
