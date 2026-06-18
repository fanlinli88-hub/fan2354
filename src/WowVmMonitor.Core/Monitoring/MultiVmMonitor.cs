namespace WowVmMonitor.Core.Monitoring;

public sealed class MultiVmMonitor
{
    public const int MaximumMachineCount = 8;

    private readonly IReadOnlyDictionary<string, MonitoredVm> _machines;

    public MultiVmMonitor(IEnumerable<MonitoredVm> machines)
    {
        ArgumentNullException.ThrowIfNull(machines);

        var materialized = machines.ToArray();
        if (materialized.Length > MaximumMachineCount)
        {
            throw new ArgumentOutOfRangeException(nameof(machines));
        }

        if (materialized
            .Select(machine => machine.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() != materialized.Length)
        {
            throw new ArgumentException("Machine identifiers must be unique.", nameof(machines));
        }

        if (materialized
            .Select(machine => machine.Monitor)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Count() != materialized.Length)
        {
            throw new ArgumentException("Each machine must own a distinct monitor instance.", nameof(machines));
        }

        if (materialized
            .Select(machine => machine.Monitor.ActivitySource)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Count() != materialized.Length)
        {
            throw new ArgumentException("Each machine must own a distinct activity source.", nameof(machines));
        }

        if (materialized
            .Select(machine => machine.Monitor.StateMachine)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Count() != materialized.Length)
        {
            throw new ArgumentException("Each machine must own a distinct state machine.", nameof(machines));
        }

        _machines = materialized.ToDictionary(machine => machine.Id, StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _machines.Count;

    public bool IsRunning(string machineId) => GetMachine(machineId).Monitor.IsRunning;

    public Task StartAsync(
        string machineId,
        Func<MultiVmMonitorResult, ValueTask> onResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onResult);

        var machine = GetMachine(machineId);
        return machine.Monitor.StartAsync(
            result => onResult(new MultiVmMonitorResult(machine.Id, machine.DisplayName, result)),
            cancellationToken);
    }

    public Task StartAllAsync(
        Func<MultiVmMonitorResult, ValueTask> onResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onResult);

        return Task.WhenAll(_machines.Values.Select(machine =>
            StartAsync(machine.Id, onResult, cancellationToken)));
    }

    public Task StopAsync(string machineId) => GetMachine(machineId).Monitor.StopAsync();

    public async Task StopAllAsync()
    {
        var stopTasks = _machines.Values
            .Select(machine => machine.Monitor.StopAsync())
            .ToArray();

        await Task.WhenAll(stopTasks).ConfigureAwait(false);
    }

    private MonitoredVm GetMachine(string machineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineId);

        return _machines.TryGetValue(machineId, out var machine)
            ? machine
            : throw new KeyNotFoundException($"Machine '{machineId}' is not registered.");
    }
}
