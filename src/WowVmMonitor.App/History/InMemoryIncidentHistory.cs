namespace WowVmMonitor.App.History;

public sealed class InMemoryIncidentHistory : IIncidentHistoryReader
{
    private readonly int _capacity;
    private readonly object _lock = new();
    private readonly LinkedList<IncidentRecord> _records = [];

    public InMemoryIncidentHistory(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public void Append(IncidentRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_lock)
        {
            _records.AddFirst(record);
            while (_records.Count > _capacity)
            {
                _records.RemoveLast();
            }
        }
    }

    public Task<IReadOnlyList<IncidentRecord>> QueryAsync(
        IncidentQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        IncidentRecord[] result;
        lock (_lock)
        {
            result = _records
                .Where(record => query.MachineId is null ||
                    string.Equals(record.MachineId, query.MachineId, StringComparison.OrdinalIgnoreCase))
                .Where(record => query.EventType is null ||
                    string.Equals(record.EventType, query.EventType, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return Task.FromResult<IReadOnlyList<IncidentRecord>>(result);
    }
}
