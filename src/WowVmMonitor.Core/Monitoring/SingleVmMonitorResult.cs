namespace WowVmMonitor.Core.Monitoring;

public sealed record SingleVmMonitorResult(
    VmMonitorStatus Status,
    MonitorTransition Transition,
    LogActivitySnapshot? Snapshot,
    TimeSpan? LogAge,
    bool IsAlerting,
    string? ErrorMessage);
