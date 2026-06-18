namespace WowVmMonitor.Core.Configuration;

public sealed record MonitorConfiguration(
    int SchemaVersion,
    MonitoringConfiguration Monitoring,
    IReadOnlyList<MachineConfiguration> Machines)
{
    public const int CurrentSchemaVersion = 1;

    public static MonitorConfiguration CreateDefault() =>
        new(CurrentSchemaVersion, new MonitoringConfiguration(60, 10, 300, 600), []);
}
