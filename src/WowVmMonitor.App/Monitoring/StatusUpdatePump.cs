using System.Collections.Concurrent;
using WowVmMonitor.App.Ui;

namespace WowVmMonitor.App.Monitoring;

public sealed class StatusUpdatePump : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, MachineStatusSnapshot> _pending =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IUiDispatcher _dispatcher;
    private readonly Action<IReadOnlyList<MachineStatusSnapshot>> _apply;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly Task _loop;

    public StatusUpdatePump(
        IUiDispatcher dispatcher,
        Action<IReadOnlyList<MachineStatusSnapshot>> apply)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(apply);
        _dispatcher = dispatcher;
        _apply = apply;
        _loop = RunAsync(_cancellation.Token);
    }

    public void Enqueue(MachineStatusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _pending[snapshot.MachineId] = snapshot;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var batch = new List<MachineStatusSnapshot>();
            foreach (var machineId in _pending.Keys)
            {
                if (_pending.TryRemove(machineId, out var snapshot))
                {
                    batch.Add(snapshot);
                }
            }

            if (batch.Count > 0)
            {
                await _dispatcher
                    .InvokeAsync(() => _apply(batch), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _flushLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }

        await FlushAsync().ConfigureAwait(false);
        _flushLock.Dispose();
        _cancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
