namespace WowVmMonitor.Core.Monitoring;

public sealed record MonitorEvaluation(
    LogActivityStatus Status,
    MonitorTransition Transition,
    TimeSpan LogAge,
    int ConsecutiveAlerts,
    int ConsecutiveNormals,
    bool IsAlerting);
