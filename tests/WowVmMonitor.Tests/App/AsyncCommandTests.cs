using WowVmMonitor.App.Mvvm;

namespace WowVmMonitor.Tests.App;

public sealed class AsyncCommandTests
{
    [Fact]
    public async Task ExecuteReturnsBeforeSlowOperationCompletesAndRejectsDuplicateExecution()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var command = new AsyncCommand(async cancellationToken =>
        {
            calls++;
            await gate.Task.WaitAsync(cancellationToken);
        });

        command.Execute(null);
        command.Execute(null);

        Assert.True(command.IsExecuting);
        Assert.Equal(1, calls);
        gate.SetResult();
        await command.ExecutionTask;
        Assert.False(command.IsExecuting);
    }

    [Fact]
    public async Task CancelStopsOperationAndResetsBusyState()
    {
        var command = new AsyncCommand(token => Task.Delay(Timeout.InfiniteTimeSpan, token));

        command.Execute(null);
        command.Cancel();
        await command.ExecutionTask;

        Assert.False(command.IsExecuting);
        Assert.True(command.CanExecute(null));
        Assert.Null(command.LastError);
    }
}
