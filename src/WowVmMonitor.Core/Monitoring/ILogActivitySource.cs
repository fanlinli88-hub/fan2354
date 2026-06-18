namespace WowVmMonitor.Core.Monitoring;

public interface ILogActivitySource
{
    ValueTask<LogActivityReadResult> ReadLatestAsync(CancellationToken cancellationToken);
}
