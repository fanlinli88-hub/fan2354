namespace WowVmMonitor.App.Monitoring;

public interface IMonitoringController
{
    bool IsRunning { get; }

    event Action<MachineStatusSnapshot>? MachineStatusChanged;

    event Action<DateTimeOffset>? CycleCompleted;

    Task CheckOnceAsync(CancellationToken cancellationToken);

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
