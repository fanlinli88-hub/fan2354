namespace WowVmMonitor.Core.Monitoring;

public sealed record LogActivitySnapshot(
    string FullPath,
    DateTimeOffset LastWriteTime);
