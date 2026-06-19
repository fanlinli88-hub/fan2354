using WowVmMonitor.App.History;
using WowVmMonitor.App.Monitoring;
using WowVmMonitor.App.Notifications;
using WowVmMonitor.App.Presentation;
using WowVmMonitor.App.Shares;
using WowVmMonitor.Core.Configuration;
using WowVmMonitor.Core.Monitoring;
using WowVmMonitor.Infrastructure.Configuration;
using WowVmMonitor.Infrastructure.Logs;

namespace WowVmMonitor.Infrastructure.DesktopServices;

public sealed class MonitoringController : IMonitoringController
{
    private readonly ConfigurationStore _configurationStore;
    private readonly IShareConnectionCoordinator _shareConnections;
    private readonly InMemoryIncidentHistory _history;
    private readonly INotificationDispatcher _notifications;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private IReadOnlyList<MonitoredVm> _machines = [];
    private IReadOnlyDictionary<string, string> _sharePaths = new Dictionary<string, string>();
    private MultiVmMonitor? _monitor;
    private int _isRunning;

    public MonitoringController(
        ConfigurationStore configurationStore,
        IShareConnectionCoordinator shareConnections,
        InMemoryIncidentHistory history,
        INotificationDispatcher? notifications = null)
    {
        _configurationStore = configurationStore;
        _shareConnections = shareConnections;
        _history = history;
        _notifications = notifications ?? new NullNotificationDispatcher();
    }

    public bool IsRunning => Volatile.Read(ref _isRunning) != 0;
    public event Action<MachineStatusSnapshot>? MachineStatusChanged;
    public event Action<DateTimeOffset>? CycleCompleted;

    public async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        var configuration = await LoadConfigurationAsync(cancellationToken).ConfigureAwait(false);
        _notifications.Configure(configuration.Ntfy);
        await ConnectSharesAsync(configuration, cancellationToken).ConfigureAwait(false);
        var machines = CreateMachines(configuration);
        var sharePaths = configuration.Machines.ToDictionary(machine => machine.Id, machine => machine.SharePath, StringComparer.OrdinalIgnoreCase);
        await Task.WhenAll(machines.Select(async machine =>
        {
            var result = await machine.Monitor
                .CheckAsync(TimeProvider.System.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            Publish(new MultiVmMonitorResult(machine.Id, machine.DisplayName, result), sharePaths[machine.Id]);
        })).ConfigureAwait(false);
        CycleCompleted?.Invoke(TimeProvider.System.GetUtcNow());
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                return;
            }

            var configuration = await LoadConfigurationAsync(cancellationToken).ConfigureAwait(false);
            _notifications.Configure(configuration.Ntfy);
            await ConnectSharesAsync(configuration, cancellationToken).ConfigureAwait(false);
            _machines = CreateMachines(configuration);
            _sharePaths = configuration.Machines.ToDictionary(machine => machine.Id, machine => machine.SharePath, StringComparer.OrdinalIgnoreCase);
            _monitor = new MultiVmMonitor(_machines);
            await _monitor.StartAllAsync(result =>
            {
                var machine = _machines.First(item => item.Id.Equals(result.MachineId, StringComparison.OrdinalIgnoreCase));
                Publish(result, _sharePaths[result.MachineId]);
                CycleCompleted?.Invoke(TimeProvider.System.GetUtcNow());
                return ValueTask.CompletedTask;
            }, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _isRunning, 1);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_monitor is not null)
            {
                await _monitor.StopAllAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            _monitor = null;
            _machines = [];
            _sharePaths = new Dictionary<string, string>();
            Volatile.Write(ref _isRunning, 0);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private Task<MonitorConfiguration> LoadConfigurationAsync(CancellationToken cancellationToken) =>
        Task.Run(() => _configurationStore.Load().Configuration, cancellationToken);

    private async Task ConnectSharesAsync(MonitorConfiguration configuration, CancellationToken cancellationToken)
    {
        var results = await _shareConnections.ConnectEnabledAsync(configuration, cancellationToken).ConfigureAwait(false);
        foreach (var result in results.Where(result => !result.Succeeded))
        {
            var machine = configuration.Machines.First(item => item.Id.Equals(result.MachineId, StringComparison.OrdinalIgnoreCase));
            MachineStatusChanged?.Invoke(new MachineStatusSnapshot(
                machine.Id, machine.DisplayName, MonitorStatusText.Format(VmMonitorStatus.ShareUnavailable), machine.SharePath,
                null, null, null, result.Code));
        }
    }

    private static IReadOnlyList<MonitoredVm> CreateMachines(MonitorConfiguration configuration)
    {
        var monitoring = configuration.Monitoring;
        return configuration.Machines.Where(machine => machine.Enabled).Select(machine =>
        {
            var source = new SharedLogActivitySource(
                machine.SharePath,
                new LatestLogLocator(),
                TimeSpan.FromSeconds(monitoring.AlertAfterSeconds));
            var stateMachine = new LogMonitorStateMachine(
                new MonitorThresholds(
                    TimeSpan.FromSeconds(monitoring.WarningAfterSeconds),
                    TimeSpan.FromSeconds(monitoring.AlertAfterSeconds)),
                requiredAlertObservations: 2,
                requiredRecoveryObservations: 2);
            var single = new SingleVmMonitor(
                source,
                stateMachine,
                TimeSpan.FromSeconds(monitoring.CheckIntervalSeconds),
                checkTimeout: TimeSpan.FromSeconds(monitoring.CheckTimeoutSeconds));
            return new MonitoredVm(machine.Id, machine.DisplayName, single);
        }).ToArray();
    }

    private void Publish(MultiVmMonitorResult result, string sharePath)
    {
        var value = result.Result;
        MachineStatusChanged?.Invoke(new MachineStatusSnapshot(
            result.MachineId,
            result.DisplayName,
            MonitorStatusText.Format(value.Status),
            sharePath,
            value.Snapshot?.FullPath,
            value.Snapshot?.LastWriteTime,
            value.LogAge,
            value.ErrorMessage));

        if (value.Transition != MonitorTransition.None)
        {
            var occurredAt = TimeProvider.System.GetUtcNow();
            _history.Append(new IncidentRecord(
                occurredAt,
                result.MachineId,
                MonitorStatusText.Format(value.Transition),
                $"{result.DisplayName}：{MonitorStatusText.Format(value.Status)}"));

            var notification = MonitorNotificationFactory.Create(result, occurredAt);
            if (notification is not null)
            {
                _notifications.TryEnqueue(notification);
            }
        }
    }

}
