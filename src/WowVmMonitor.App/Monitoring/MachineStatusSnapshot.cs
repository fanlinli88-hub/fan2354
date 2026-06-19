namespace WowVmMonitor.App.Monitoring;

public sealed record MachineStatusSnapshot(
    string MachineId,
    string DisplayName,
    string StatusText,
    string SharePath,
    string? LatestLogPath,
    DateTimeOffset? LastWriteTime,
    TimeSpan? LogAge,
    string? ErrorCode);
