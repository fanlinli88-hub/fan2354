namespace WowVmMonitor.Core.Monitoring;

public sealed record MonitorThresholds
{
    public MonitorThresholds(TimeSpan WarningAfter, TimeSpan AlertAfter)
    {
        if (WarningAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(WarningAfter));
        }

        if (AlertAfter <= WarningAfter)
        {
            throw new ArgumentException("Alert threshold must be greater than warning threshold.", nameof(AlertAfter));
        }

        this.WarningAfter = WarningAfter;
        this.AlertAfter = AlertAfter;
    }

    public TimeSpan WarningAfter { get; }

    public TimeSpan AlertAfter { get; }
}
