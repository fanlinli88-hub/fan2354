namespace WowVmMonitor.Core.Monitoring;

public sealed record MultiVmMonitorResult(
    string MachineId,
    string DisplayName,
    SingleVmMonitorResult Result);
