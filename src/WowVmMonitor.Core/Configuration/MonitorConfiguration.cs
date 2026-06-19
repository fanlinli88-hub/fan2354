using System.Text.Json.Serialization;

namespace WowVmMonitor.Core.Configuration;

public sealed record MonitorConfiguration
{
    public const int CurrentSchemaVersion = 2;

    [JsonConstructor]
    public MonitorConfiguration(
        int schemaVersion,
        MonitoringConfiguration monitoring,
        IReadOnlyList<MachineConfiguration> machines,
        NtfyConfiguration ntfy)
    {
        SchemaVersion = schemaVersion;
        Monitoring = monitoring;
        Machines = machines;
        Ntfy = ntfy;
    }

    public MonitorConfiguration(
        int schemaVersion,
        MonitoringConfiguration monitoring,
        IReadOnlyList<MachineConfiguration> machines)
        : this(schemaVersion, monitoring, machines, NtfyConfiguration.CreateDefault())
    {
    }

    public int SchemaVersion { get; init; }

    public MonitoringConfiguration Monitoring { get; init; }

    public IReadOnlyList<MachineConfiguration> Machines { get; init; }

    public NtfyConfiguration Ntfy { get; init; }

    public static MonitorConfiguration CreateDefault() =>
        new(CurrentSchemaVersion, new MonitoringConfiguration(60, 10, 300, 600), [], NtfyConfiguration.CreateDefault());
}
