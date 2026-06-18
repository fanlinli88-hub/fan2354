using System.Diagnostics;
using WowVmMonitor.Core.Monitoring;

namespace WowVmMonitor.Tests.Core;

public sealed class MultiVmMonitorResourceTests
{
    [Fact]
    [Trait("Category", "Soak")]
    public async Task EightIdleMonitorsDoNotBusyLoopOrRetainUnboundedMemory()
    {
        var sources = Enumerable.Range(1, 8).Select(_ => new CountingSource()).ToArray();
        var machines = sources.Select((source, index) => CreateMachine(index + 1, source)).ToArray();
        var monitor = new MultiVmMonitor(machines);
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var stopwatch = Stopwatch.StartNew();

        await monitor.StartAllAsync(_ => ValueTask.CompletedTask);
        await Task.Delay(TimeSpan.FromSeconds(2));
        await monitor.StopAllAsync();
        stopwatch.Stop();
        process.Refresh();

        var cpuPercent = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds /
                         stopwatch.Elapsed.TotalMilliseconds /
                         Environment.ProcessorCount * 100;

        Assert.All(sources, source => Assert.Equal(1, source.ReadCount));
        Assert.True(cpuPercent < 5, $"Average CPU usage was {cpuPercent:N2}%.");
        Assert.True(
            process.WorkingSet64 < 150L * 1024 * 1024,
            $"Working set was {process.WorkingSet64 / 1024d / 1024d:N2} MB.");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);

        for (var cycle = 0; cycle < 250; cycle++)
        {
            await monitor.StartAllAsync(_ => ValueTask.CompletedTask);
            await Task.Delay(1);
            await monitor.StopAllAsync();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var retainedGrowth = GC.GetTotalMemory(forceFullCollection: true) - memoryBefore;

        Assert.True(
            retainedGrowth < 4 * 1024 * 1024,
            $"Retained memory grew by {retainedGrowth:N0} bytes.");
    }

    private static MonitoredVm CreateMachine(int number, CountingSource source)
    {
        var stateMachine = new LogMonitorStateMachine(
            new MonitorThresholds(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)),
            requiredAlertObservations: 2,
            requiredRecoveryObservations: 2);
        var singleMonitor = new SingleVmMonitor(
            source,
            stateMachine,
            interval: TimeSpan.FromSeconds(60),
            checkTimeout: TimeSpan.FromSeconds(10));
        return new MonitoredVm($"vm-{number}", $"VM {number}", singleMonitor);
    }

    private sealed class CountingSource : ILogActivitySource
    {
        private int _readCount;

        public int ReadCount => Volatile.Read(ref _readCount);

        public ValueTask<LogActivityReadResult> ReadLatestAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readCount);
            return ValueTask.FromResult(LogActivityReadResult.Available(
                new LogActivitySnapshot("activity.log", DateTimeOffset.UtcNow)));
        }
    }
}
