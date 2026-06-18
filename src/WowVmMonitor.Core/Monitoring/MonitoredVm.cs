namespace WowVmMonitor.Core.Monitoring;

public sealed record MonitoredVm
{
    public MonitoredVm(string id, string displayName, SingleVmMonitor monitor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(monitor);

        Id = id;
        DisplayName = displayName;
        Monitor = monitor;
    }

    public string Id { get; }

    public string DisplayName { get; init; }

    public SingleVmMonitor Monitor { get; }
}
