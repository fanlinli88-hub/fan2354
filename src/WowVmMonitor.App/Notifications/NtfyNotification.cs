namespace WowVmMonitor.App.Notifications;

public enum NtfyNotificationKind
{
    Alert,
    Recovery
}

public sealed record NtfyNotification(
    NtfyNotificationKind Kind,
    string MachineDisplayName,
    DateTimeOffset OccurredAt,
    TimeSpan? LogAge);
