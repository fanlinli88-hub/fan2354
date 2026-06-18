namespace WowVmMonitor.Infrastructure.Logs;

public sealed record LatestLogFile(
    string FullPath,
    DateTimeOffset LastWriteTime,
    bool FromCache);
