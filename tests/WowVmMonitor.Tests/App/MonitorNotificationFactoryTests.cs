using WowVmMonitor.App.Notifications;
using WowVmMonitor.Core.Monitoring;

namespace WowVmMonitor.Tests.App;

public sealed class MonitorNotificationFactoryTests
{
    [Theory]
    [InlineData(MonitorTransition.Alert, NtfyNotificationKind.Alert)]
    [InlineData(MonitorTransition.Recovery, NtfyNotificationKind.Recovery)]
    public void TransitionCreatesExpectedNotification(
        MonitorTransition transition,
        NtfyNotificationKind expectedKind)
    {
        var result = CreateResult(VmMonitorStatus.Alert, transition);
        var occurredAt = new DateTimeOffset(2026, 6, 19, 3, 1, 28, TimeSpan.Zero);

        var notification = MonitorNotificationFactory.Create(result, occurredAt);

        Assert.NotNull(notification);
        Assert.Equal(expectedKind, notification.Kind);
        Assert.Equal("VM 01", notification.MachineDisplayName);
        Assert.Equal(occurredAt, notification.OccurredAt);
        Assert.Equal(TimeSpan.FromMinutes(10), notification.LogAge);
    }

    [Theory]
    [InlineData(VmMonitorStatus.Normal)]
    [InlineData(VmMonitorStatus.Warning)]
    [InlineData(VmMonitorStatus.ShareUnavailable)]
    [InlineData(VmMonitorStatus.NoLog)]
    [InlineData(VmMonitorStatus.Error)]
    public void NoTransitionCreatesNoNotification(VmMonitorStatus status) =>
        Assert.Null(MonitorNotificationFactory.Create(
            CreateResult(status, MonitorTransition.None),
            DateTimeOffset.UtcNow));

    private static MultiVmMonitorResult CreateResult(
        VmMonitorStatus status,
        MonitorTransition transition) =>
        new("vm-01", "VM 01", new SingleVmMonitorResult(
            status,
            transition,
            null,
            TimeSpan.FromMinutes(10),
            transition == MonitorTransition.Alert,
            null));
}
