namespace WowVmMonitor.App.History;

public interface IIncidentHistoryReader
{
    Task<IReadOnlyList<IncidentRecord>> QueryAsync(
        IncidentQuery query,
        CancellationToken cancellationToken);
}
