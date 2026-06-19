using WowVmMonitor.Core.Monitoring;

namespace WowVmMonitor.App.Notifications;

public static class MonitorNotificationFactory
{
    public static NtfyNotification? Create(
        MultiVmMonitorResult result,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(result);
        var kind = result.Result.Transition switch
        {
            MonitorTransition.Alert => NtfyNotificationKind.Alert,
            MonitorTransition.Recovery => NtfyNotificationKind.Recovery,
            MonitorTransition.None => (NtfyNotificationKind?)null,
            _ => null
        };
        return kind is null
            ? null
            : new NtfyNotification(
                kind.Value,
                result.DisplayName,
                occurredAt,
                result.Result.LogAge);
    }
}
