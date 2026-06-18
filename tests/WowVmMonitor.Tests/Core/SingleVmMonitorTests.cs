using WowVmMonitor.Core.Monitoring;

namespace WowVmMonitor.Tests.Core;

public sealed class SingleVmMonitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(4, 59, VmMonitorStatus.Normal)]
    [InlineData(5, 0, VmMonitorStatus.Warning)]
    [InlineData(10, 0, VmMonitorStatus.Alert)]
    public async Task ReportsNormalWarningAndAlertStates(int minutes, int seconds, VmMonitorStatus expected)
    {
        var source = new StubLogActivitySource(LogActivityReadResult.Available(
            new LogActivitySnapshot("test.log", Now.AddMinutes(-minutes).AddSeconds(-seconds))));
        var monitor = CreateMonitor(source);

        var result = await monitor.CheckAsync(Now);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task ReportsRecoveryAfterTwoConsecutiveNormalChecks()
    {
        var source = new StubLogActivitySource(
            LogActivityReadResult.Available(new LogActivitySnapshot("stale.log", Now.AddMinutes(-10))),
            LogActivityReadResult.Available(new LogActivitySnapshot("stale.log", Now.AddMinutes(-10))),
            LogActivityReadResult.Available(new LogActivitySnapshot("active.log", Now.AddMinutes(1))),
            LogActivityReadResult.Available(new LogActivitySnapshot("active.log", Now.AddMinutes(2))));
        var monitor = CreateMonitor(source);

        await monitor.CheckAsync(Now);
        var alert = await monitor.CheckAsync(Now.AddMinutes(1));
        var firstNormal = await monitor.CheckAsync(Now.AddMinutes(2));
        var recovery = await monitor.CheckAsync(Now.AddMinutes(3));

        Assert.Equal(MonitorTransition.Alert, alert.Transition);
        Assert.Equal(VmMonitorStatus.Normal, firstNormal.Status);
        Assert.Equal(VmMonitorStatus.Recovery, recovery.Status);
        Assert.Equal(MonitorTransition.Recovery, recovery.Transition);
    }

    [Fact]
    public async Task ShareUnavailableDoesNotThrowOrChangePreviousAlertState()
    {
        var source = new StubLogActivitySource(
            LogActivityReadResult.Available(new LogActivitySnapshot("stale.log", Now.AddMinutes(-10))),
            LogActivityReadResult.Available(new LogActivitySnapshot("stale.log", Now.AddMinutes(-10))),
            LogActivityReadResult.ShareUnavailable("network unavailable"));
        var monitor = CreateMonitor(source);

        await monitor.CheckAsync(Now);
        await monitor.CheckAsync(Now.AddMinutes(1));
        var unavailable = await monitor.CheckAsync(Now.AddMinutes(2));

        Assert.Equal(VmMonitorStatus.ShareUnavailable, unavailable.Status);
        Assert.True(unavailable.IsAlerting);
        Assert.Equal("network unavailable", unavailable.ErrorMessage);
    }

    [Fact]
    public async Task CanStartAndStopMonitoringLoop()
    {
        var source = new StubLogActivitySource(LogActivityReadResult.Available(
            new LogActivitySnapshot("active.log", Now)));
        var monitor = CreateMonitor(source, TimeSpan.FromMilliseconds(5));
        var firstResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await monitor.StartAsync(
            _ =>
            {
                firstResult.TrySetResult();
                return ValueTask.CompletedTask;
            });

        await firstResult.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await monitor.StopAsync();
        var countAfterStop = source.ReadCount;
        await Task.Delay(30);

        Assert.False(monitor.IsRunning);
        Assert.Equal(countAfterStop, source.ReadCount);
    }

    [Fact]
    public async Task ExternalCancellationStopsMonitoringLoop()
    {
        var source = new StubLogActivitySource(LogActivityReadResult.Available(
            new LogActivitySnapshot("active.log", Now)));
        var monitor = CreateMonitor(source, TimeSpan.FromMilliseconds(5));
        var firstResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        await monitor.StartAsync(
            _ =>
            {
                firstResult.TrySetResult();
                return ValueTask.CompletedTask;
            },
            cancellation.Token);

        await firstResult.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await monitor.StopAsync();

        Assert.False(monitor.IsRunning);
    }

    [Fact]
    public async Task CheckTimeoutReturnsShareUnavailableWithoutWaitingForSource()
    {
        var source = new BlockingLogActivitySource();
        var monitor = CreateMonitor(
            source,
            interval: TimeSpan.FromMinutes(1),
            checkTimeout: TimeSpan.FromMilliseconds(25));

        var result = await monitor.CheckAsync(Now).AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(VmMonitorStatus.ShareUnavailable, result.Status);
        Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        source.Release();
    }

    [Fact]
    [Trait("Category", "Soak")]
    public async Task RepeatedChecksDoNotRetainUnboundedMemory()
    {
        var source = new StubLogActivitySource(LogActivityReadResult.Available(
            new LogActivitySnapshot("active.log", Now)));
        var monitor = CreateMonitor(source);

        for (var index = 0; index < 100; index++)
        {
            await monitor.CheckAsync(Now);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);

        for (var index = 0; index < 20_000; index++)
        {
            await monitor.CheckAsync(Now);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memoryAfter = GC.GetTotalMemory(forceFullCollection: true);

        Assert.True(
            memoryAfter - memoryBefore < 2 * 1024 * 1024,
            $"Retained memory grew by {memoryAfter - memoryBefore:N0} bytes.");
    }

    private static SingleVmMonitor CreateMonitor(
        ILogActivitySource source,
        TimeSpan? interval = null,
        TimeSpan? checkTimeout = null) =>
        new(
            source,
            new LogMonitorStateMachine(
                new MonitorThresholds(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)),
                requiredAlertObservations: 2,
                requiredRecoveryObservations: 2),
            interval ?? TimeSpan.FromMinutes(1),
            checkTimeout: checkTimeout);

    private sealed class BlockingLogActivitySource : ILogActivitySource
    {
        private readonly TaskCompletionSource<LogActivityReadResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<LogActivityReadResult> ReadLatestAsync(CancellationToken cancellationToken) =>
            new(_completion.Task);

        public void Release() => _completion.TrySetResult(LogActivityReadResult.NoLog());
    }

    private sealed class StubLogActivitySource(params LogActivityReadResult[] results) : ILogActivitySource
    {
        private readonly LogActivityReadResult[] _results = results;
        private int _index;

        public int ReadCount { get; private set; }

        public ValueTask<LogActivityReadResult> ReadLatestAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            var result = _results[Math.Min(_index, _results.Length - 1)];
            _index++;
            return ValueTask.FromResult(result);
        }
    }
}
