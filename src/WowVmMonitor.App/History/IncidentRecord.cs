namespace WowVmMonitor.App.History;

public sealed record IncidentRecord(
    DateTimeOffset Timestamp,
    string MachineId,
    string EventType,
    string Message);
