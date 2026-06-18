using System.Collections.ObjectModel;
using WowVmMonitor.App.Mvvm;
using WowVmMonitor.App.Ui;

namespace WowVmMonitor.App.Monitoring;

public sealed class MonitoringDashboardViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IMonitoringController _controller;
    private readonly IUiDispatcher _dispatcher;
    private readonly StatusUpdatePump _statusPump;
    private bool _requiresUserConfirmation;
    private DateTimeOffset? _lastCompletedCheck;

    public MonitoringDashboardViewModel(
        IMonitoringController controller,
        IUiDispatcher dispatcher,
        bool requiresUserConfirmation = false)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _controller = controller;
        _dispatcher = dispatcher;
        _requiresUserConfirmation = requiresUserConfirmation;
        _statusPump = new StatusUpdatePump(dispatcher, ApplyStatusBatch);
        StartCommand = new AsyncCommand(StartAsync, CanStart);
        StopCommand = new AsyncCommand(StopAsync, CanStop);
        CheckOnceCommand = new AsyncCommand(CheckOnceAsync, () => !RequiresUserConfirmation);
        _controller.MachineStatusChanged += OnMachineStatusChanged;
        _controller.CycleCompleted += OnCycleCompleted;
    }

    public ObservableCollection<MachineStatusViewModel> Machines { get; } = [];
    public AsyncCommand StartCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand CheckOnceCommand { get; }

    public bool RequiresUserConfirmation
    {
        get => _requiresUserConfirmation;
        set
        {
            if (SetProperty(ref _requiresUserConfirmation, value))
            {
                RefreshCommands();
            }
        }
    }

    public DateTimeOffset? LastCompletedCheck
    {
        get => _lastCompletedCheck;
        private set => SetProperty(ref _lastCompletedCheck, value);
    }

    public Task FlushStatusUpdatesAsync(CancellationToken cancellationToken = default) =>
        _statusPump.FlushAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _controller.MachineStatusChanged -= OnMachineStatusChanged;
        _controller.CycleCompleted -= OnCycleCompleted;
        StartCommand.Dispose();
        StopCommand.Dispose();
        CheckOnceCommand.Dispose();
        await _statusPump.DisposeAsync().ConfigureAwait(false);
    }

    private bool CanStart() => !RequiresUserConfirmation && !_controller.IsRunning;
    private bool CanStop() => _controller.IsRunning;

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _controller.StartAsync(cancellationToken);
        }
        finally
        {
            RefreshCommands();
        }
    }

    private async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _controller.StopAsync(cancellationToken);
        }
        finally
        {
            RefreshCommands();
        }
    }

    private Task CheckOnceAsync(CancellationToken cancellationToken) =>
        _controller.CheckOnceAsync(cancellationToken);

    private void OnMachineStatusChanged(MachineStatusSnapshot snapshot) =>
        _statusPump.Enqueue(snapshot);

    private void OnCycleCompleted(DateTimeOffset completedAt) =>
        _ = _dispatcher.InvokeAsync(() => LastCompletedCheck = completedAt);

    private void ApplyStatusBatch(IReadOnlyList<MachineStatusSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            var existing = Machines.FirstOrDefault(machine =>
                string.Equals(machine.Id, snapshot.MachineId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                Machines.Add(new MachineStatusViewModel(snapshot));
            }
            else
            {
                existing.Update(snapshot);
            }
        }
    }

    private void RefreshCommands()
    {
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        CheckOnceCommand.RaiseCanExecuteChanged();
    }
}
