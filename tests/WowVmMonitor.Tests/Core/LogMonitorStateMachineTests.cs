using WowVmMonitor.Core.Monitoring;

namespace WowVmMonitor.Tests.Core;

public sealed class LogMonitorStateMachineTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(4, 59, LogActivityStatus.Normal)]
    [InlineData(5, 0, LogActivityStatus.Warning)]
    [InlineData(9, 59, LogActivityStatus.Warning)]
    [InlineData(10, 0, LogActivityStatus.Alert)]
    public void ClassifiesLogAgeAtExactBoundaries(int minutes, int seconds, LogActivityStatus expected)
    {
        var monitor = CreateMonitor();

        var result = monitor.Evaluate(Now, Now.AddMinutes(-minutes).AddSeconds(-seconds));

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void RequiresTwoConsecutiveAlertObservationsBeforeAlertTransition()
    {
        var monitor = CreateMonitor();

        var first = monitor.Evaluate(Now, Now.AddMinutes(-10));
        var second = monitor.Evaluate(Now.AddMinutes(1), Now.AddMinutes(-10));

        Assert.Equal(MonitorTransition.None, first.Transition);
        Assert.Equal(MonitorTransition.Alert, second.Transition);
        Assert.True(second.IsAlerting);
    }

    [Fact]
    public void WarningBreaksConsecutiveAlertSequence()
    {
        var monitor = CreateMonitor();

        monitor.Evaluate(Now, Now.AddMinutes(-10));
        monitor.Evaluate(Now.AddSeconds(30), Now.AddMinutes(-9));
        var result = monitor.Evaluate(Now.AddMinutes(1), Now.AddMinutes(-10));

        Assert.Equal(MonitorTransition.None, result.Transition);
        Assert.False(result.IsAlerting);
        Assert.Equal(1, result.ConsecutiveAlerts);
    }

    [Fact]
    public void RequiresTwoConsecutiveNormalObservationsBeforeRecovery()
    {
        var monitor = CreateMonitor();
        monitor.Evaluate(Now, Now.AddMinutes(-10));
        monitor.Evaluate(Now.AddMinutes(1), Now.AddMinutes(-10));

        var firstNormal = monitor.Evaluate(Now.AddMinutes(2), Now.AddMinutes(1));
        var secondNormal = monitor.Evaluate(Now.AddMinutes(3), Now.AddMinutes(2));

        Assert.Equal(MonitorTransition.None, firstNormal.Transition);
        Assert.True(firstNormal.IsAlerting);
        Assert.Equal(MonitorTransition.Recovery, secondNormal.Transition);
        Assert.False(secondNormal.IsAlerting);
    }

    [Fact]
    public void WarningDoesNotRecoverAnActiveAlert()
    {
        var monitor = CreateMonitor();
        monitor.Evaluate(Now, Now.AddMinutes(-10));
        monitor.Evaluate(Now.AddMinutes(1), Now.AddMinutes(-10));

        var warning = monitor.Evaluate(Now.AddMinutes(2), Now.AddMinutes(-7));

        Assert.Equal(LogActivityStatus.Warning, warning.Status);
        Assert.Equal(MonitorTransition.None, warning.Transition);
        Assert.True(warning.IsAlerting);
    }

    [Fact]
    public void RejectsInvalidThresholdOrder()
    {
        Assert.Throws<ArgumentException>(() => new MonitorThresholds(
            WarningAfter: TimeSpan.FromMinutes(10),
            AlertAfter: TimeSpan.FromMinutes(10)));
    }

    private static LogMonitorStateMachine CreateMonitor() => new(
        new MonitorThresholds(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)),
        requiredAlertObservations: 2,
        requiredRecoveryObservations: 2);
}
