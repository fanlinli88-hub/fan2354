namespace WowVmMonitor.Core.Monitoring;

public sealed class SingleVmMonitor
{
    private readonly ILogActivitySource _source;
    private readonly LogMonitorStateMachine _stateMachine;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _checkTimeout;
    private readonly TimeProvider _timeProvider;
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;

    public SingleVmMonitor(
        ILogActivitySource source,
        LogMonitorStateMachine stateMachine,
        TimeSpan interval,
        TimeProvider? timeProvider = null,
        TimeSpan? checkTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(stateMachine);

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        if (checkTimeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(checkTimeout));
        }

        _source = source;
        _stateMachine = stateMachine;
        _interval = interval;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _checkTimeout = checkTimeout ?? TimeSpan.FromSeconds(10);
    }

    public bool IsRunning
    {
        get
        {
            lock (_lifecycleLock)
            {
                return _runTask is { IsCompleted: false };
            }
        }
    }

    internal ILogActivitySource ActivitySource => _source;

    internal LogMonitorStateMachine StateMachine => _stateMachine;

    public async ValueTask<SingleVmMonitorResult> CheckAsync(
        DateTimeOffset currentTime,
        CancellationToken cancellationToken = default)
    {
        LogActivityReadResult readResult;
        try
        {
            readResult = await _source
                .ReadLatestAsync(cancellationToken)
                .AsTask()
                .WaitAsync(_checkTimeout, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new SingleVmMonitorResult(
                VmMonitorStatus.ShareUnavailable,
                MonitorTransition.None,
                null,
                null,
                _stateMachine.IsAlerting,
                $"Log activity check timed out after {_checkTimeout.TotalSeconds:N0} seconds.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new SingleVmMonitorResult(
                VmMonitorStatus.Error,
                MonitorTransition.None,
                null,
                null,
                _stateMachine.IsAlerting,
                exception.Message);
        }

        if (readResult.Status == LogActivityReadStatus.ShareUnavailable)
        {
            return new SingleVmMonitorResult(
                VmMonitorStatus.ShareUnavailable,
                MonitorTransition.None,
                null,
                null,
                _stateMachine.IsAlerting,
                readResult.ErrorMessage);
        }

        if (readResult.Status == LogActivityReadStatus.NoLog || readResult.Snapshot is null)
        {
            return new SingleVmMonitorResult(
                VmMonitorStatus.NoLog,
                MonitorTransition.None,
                null,
                null,
                _stateMachine.IsAlerting,
                null);
        }

        var evaluation = _stateMachine.Evaluate(currentTime, readResult.Snapshot.LastWriteTime);
        var status = evaluation.Transition == MonitorTransition.Recovery
            ? VmMonitorStatus.Recovery
            : evaluation.Status switch
            {
                LogActivityStatus.Normal => VmMonitorStatus.Normal,
                LogActivityStatus.Warning => VmMonitorStatus.Warning,
                LogActivityStatus.Alert => VmMonitorStatus.Alert,
                _ => throw new InvalidOperationException("Unsupported activity status.")
            };

        return new SingleVmMonitorResult(
            status,
            evaluation.Transition,
            readResult.Snapshot,
            evaluation.LogAge,
            evaluation.IsAlerting,
            null);
    }

    public Task StartAsync(
        Func<SingleVmMonitorResult, ValueTask> onResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onResult);

        lock (_lifecycleLock)
        {
            if (_runTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("The monitor is already running.");
            }

            _runCancellation?.Dispose();
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runTask = RunLoopAsync(onResult, _runCancellation.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? runTask;
        CancellationTokenSource? cancellation;

        lock (_lifecycleLock)
        {
            runTask = _runTask;
            cancellation = _runCancellation;
        }

        if (runTask is null || cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        try
        {
            await runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    private async Task RunLoopAsync(
        Func<SingleVmMonitorResult, ValueTask> onResult,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await CheckAsync(_timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            await onResult(result).ConfigureAwait(false);
            await Task.Delay(_interval, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
