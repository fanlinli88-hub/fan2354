namespace WowVmMonitor.Core.Monitoring;

public sealed record LogActivityReadResult
{
    private LogActivityReadResult(
        LogActivityReadStatus status,
        LogActivitySnapshot? snapshot,
        string? errorMessage)
    {
        Status = status;
        Snapshot = snapshot;
        ErrorMessage = errorMessage;
    }

    public LogActivityReadStatus Status { get; }

    public LogActivitySnapshot? Snapshot { get; }

    public string? ErrorMessage { get; }

    public static LogActivityReadResult Available(LogActivitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new LogActivityReadResult(LogActivityReadStatus.Available, snapshot, null);
    }

    public static LogActivityReadResult ShareUnavailable(string? errorMessage = null) =>
        new(LogActivityReadStatus.ShareUnavailable, null, errorMessage);

    public static LogActivityReadResult NoLog() =>
        new(LogActivityReadStatus.NoLog, null, null);
}
