namespace WowVmMonitor.Core.Configuration;

public sealed record MonitoringConfiguration(
    int CheckIntervalSeconds,
    int CheckTimeoutSeconds,
    int WarningAfterSeconds,
    int AlertAfterSeconds);
