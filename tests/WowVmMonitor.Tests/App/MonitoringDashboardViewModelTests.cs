using WowVmMonitor.App.Monitoring;
using WowVmMonitor.App.Ui;

namespace WowVmMonitor.Tests.App;

public sealed class MonitoringDashboardViewModelTests
{
    [Fact]
    public async Task SlowStartDoesNotBlockAndStatusUpdatesUseDispatcher()
    {
        var controller = new BlockingMonitoringController();
        var dispatcher = new RecordingDispatcher();
        await using var viewModel = new MonitoringDashboardViewModel(controller, dispatcher);

        viewModel.StartCommand.Execute(null);
        controller.Publish(new MachineStatusSnapshot(
            "vm-01",
            "VM 01",
            "Normal",
            @"\\server\share",
            "activity.log",
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(5),
            null));

        Assert.True(viewModel.StartCommand.IsExecuting);
        controller.ReleaseStart();
        await viewModel.StartCommand.ExecutionTask;
        await viewModel.FlushStatusUpdatesAsync();

        Assert.True(dispatcher.InvokeCount > 0);
        Assert.Equal("Normal", viewModel.Machines.Single().StatusText);
    }

    private sealed class BlockingMonitoringController : IMonitoringController
    {
        private readonly TaskCompletionSource _start =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsRunning { get; private set; }

        public event Action<MachineStatusSnapshot>? MachineStatusChanged;

        public event Action<DateTimeOffset>? CycleCompleted
        {
            add { }
            remove { }
        }

        public Task CheckOnceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _start.Task.WaitAsync(cancellationToken);
            IsRunning = true;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public void Publish(MachineStatusSnapshot snapshot) => MachineStatusChanged?.Invoke(snapshot);

        public void ReleaseStart() => _start.TrySetResult();
    }

    private sealed class RecordingDispatcher : IUiDispatcher
    {
        public int InvokeCount { get; private set; }

        public bool CheckAccess() => false;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvokeCount++;
            action();
            return Task.CompletedTask;
        }
    }
}
