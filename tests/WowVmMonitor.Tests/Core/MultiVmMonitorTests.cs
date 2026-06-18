using WowVmMonitor.Core.Monitoring;

namespace WowVmMonitor.Tests.Core;

public sealed class MultiVmMonitorTests
{
    [Fact]
    public void AcceptsEightDistinctMachines()
    {
        var machines = Enumerable.Range(1, 8).Select(CreateMachine).ToArray();

        var monitor = new MultiVmMonitor(machines);

        Assert.Equal(8, monitor.Count);
    }

    [Fact]
    public void RejectsNinthMachine()
    {
        var machines = Enumerable.Range(1, 9).Select(CreateMachine).ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() => new MultiVmMonitor(machines));
    }

    [Fact]
    public void RejectsDuplicateIdentityOrMonitorInstance()
    {
        var first = CreateMachine(1);

        Assert.Throws<ArgumentException>(() => new MultiVmMonitor(
            [first, first with { DisplayName = "duplicate-name" }]));
    }

    [Fact]
    public void RejectsSharedActivitySource()
    {
        var source = new ConstantSource(1);
        var first = CreateMachine("vm-1", source, CreateStateMachine());
        var second = CreateMachine("vm-2", source, CreateStateMachine());

        Assert.Throws<ArgumentException>(() => new MultiVmMonitor([first, second]));
    }

    [Fact]
    public void RejectsSharedStateMachine()
    {
        var stateMachine = CreateStateMachine();
        var first = CreateMachine("vm-1", new ConstantSource(1), stateMachine);
        var second = CreateMachine("vm-2", new ConstantSource(2), stateMachine);

        Assert.Throws<ArgumentException>(() => new MultiVmMonitor([first, second]));
    }

    [Fact]
    public async Task StartsEightMachinesAndTagsTheirResults()
    {
        var sources = Enumerable.Range(1, 8).Select(number => new CountingSource(number)).ToArray();
        var monitor = new MultiVmMonitor(sources.Select(source => CreateMachine(source, TimeSpan.FromMinutes(1))));
        var results = new System.Collections.Concurrent.ConcurrentDictionary<string, MultiVmMonitorResult>();

        await monitor.StartAllAsync(result =>
        {
            results[result.MachineId] = result;
            return ValueTask.CompletedTask;
        });

        await WaitUntilAsync(() => results.Count == 8);
        await monitor.StopAllAsync();

        Assert.Equal(8, results.Count);
        Assert.All(sources, source => Assert.True(source.ReadCount >= 1));
        Assert.All(Enumerable.Range(1, 8), number =>
            Assert.Equal($"VM {number}", results[$"vm-{number}"].DisplayName));
    }

    [Fact]
    public async Task StoppingOneMachineLeavesOtherSevenRunning()
    {
        var sources = Enumerable.Range(1, 8).Select(number => new CountingSource(number)).ToArray();
        var monitor = new MultiVmMonitor(
            sources.Select(source => CreateMachine(source, TimeSpan.FromMilliseconds(10))));

        await monitor.StartAllAsync(_ => ValueTask.CompletedTask);
        await WaitUntilAsync(() => sources.All(source => source.ReadCount >= 1));
        await monitor.StopAsync("vm-1");
        var countsAfterStop = sources.Select(source => source.ReadCount).ToArray();
        await WaitUntilAsync(() => sources.Skip(1).All(source =>
            source.ReadCount > countsAfterStop[source.Number - 1]));
        await monitor.StopAllAsync();

        Assert.False(monitor.IsRunning("vm-1"));
        Assert.Equal(countsAfterStop[0], sources[0].ReadCount);
        Assert.All(sources.Skip(1), source =>
            Assert.True(source.ReadCount > countsAfterStop[source.Number - 1]));
    }

    [Fact]
    public async Task OneTimedOutMachineDoesNotDelayOtherSeven()
    {
        var blocked = new BlockingLogActivitySource();
        var fastSources = Enumerable.Range(2, 7).Select(number => new CountingSource(number)).ToArray();
        var machines = new[]
            {
                CreateMachine(
                    "vm-1",
                    blocked,
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromMilliseconds(25))
            }
            .Concat(fastSources.Select(source => CreateMachine(source, TimeSpan.FromMinutes(1))));
        var monitor = new MultiVmMonitor(machines);
        var results = new System.Collections.Concurrent.ConcurrentDictionary<string, MultiVmMonitorResult>();

        await monitor.StartAllAsync(result =>
        {
            results[result.MachineId] = result;
            return ValueTask.CompletedTask;
        });

        await WaitUntilAsync(() => results.Count == 8);
        await monitor.StopAllAsync();
        blocked.Release();

        Assert.Equal(VmMonitorStatus.ShareUnavailable, results["vm-1"].Result.Status);
        Assert.All(Enumerable.Range(2, 7), number =>
            Assert.Equal(VmMonitorStatus.Normal, results[$"vm-{number}"].Result.Status));
    }

    [Fact]
    public async Task AlertCountersRemainIndependent()
    {
        var now = DateTimeOffset.UtcNow;
        var stale = CreateMachine(
            "stale",
            new ConstantTimestampSource(now.AddMinutes(-10)),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromSeconds(1));
        var active = CreateMachine(
            "active",
            new ConstantTimestampSource(now),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromSeconds(1));

        var firstStale = await stale.Monitor.CheckAsync(now);
        var activeResult = await active.Monitor.CheckAsync(now);
        var secondStale = await stale.Monitor.CheckAsync(now);

        Assert.False(firstStale.IsAlerting);
        Assert.False(activeResult.IsAlerting);
        Assert.True(secondStale.IsAlerting);
    }

    private static MonitoredVm CreateMachine(int number)
    {
        var source = new ConstantSource(number);
        var monitor = new SingleVmMonitor(
            source,
            new LogMonitorStateMachine(
                new MonitorThresholds(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)),
                2,
                2),
            TimeSpan.FromMinutes(1));
        return new MonitoredVm($"vm-{number}", $"VM {number}", monitor);
    }

    private static MonitoredVm CreateMachine(
        string id,
        ILogActivitySource source,
        LogMonitorStateMachine stateMachine) =>
        new(id, id.ToUpperInvariant(), new SingleVmMonitor(source, stateMachine, TimeSpan.FromMinutes(1)));

    private static LogMonitorStateMachine CreateStateMachine() =>
        new(
            new MonitorThresholds(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)),
            requiredAlertObservations: 2,
            requiredRecoveryObservations: 2);

    private static MonitoredVm CreateMachine(CountingSource source, TimeSpan interval)
    {
        var monitor = new SingleVmMonitor(
            source,
            new LogMonitorStateMachine(
                new MonitorThresholds(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)),
                2,
                2),
            interval);
        return new MonitoredVm($"vm-{source.Number}", $"VM {source.Number}", monitor);
    }

    private static MonitoredVm CreateMachine(
        string id,
        ILogActivitySource source,
        TimeSpan interval,
        TimeSpan checkTimeout)
    {
        var monitor = new SingleVmMonitor(
            source,
            new LogMonitorStateMachine(
                new MonitorThresholds(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)),
                2,
                2),
            interval,
            checkTimeout: checkTimeout);
        return new MonitoredVm(id, id.ToUpperInvariant(), monitor);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }

        Assert.True(condition(), "Condition was not reached before the test deadline.");
    }

    private sealed class ConstantSource(int number) : ILogActivitySource
    {
        public ValueTask<LogActivityReadResult> ReadLatestAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(LogActivityReadResult.Available(
                new LogActivitySnapshot($"vm-{number}.log", DateTimeOffset.UtcNow)));
    }

    private sealed class CountingSource(int number) : ILogActivitySource
    {
        private int _readCount;

        public int Number { get; } = number;

        public int ReadCount => Volatile.Read(ref _readCount);

        public ValueTask<LogActivityReadResult> ReadLatestAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readCount);
            return ValueTask.FromResult(LogActivityReadResult.Available(
                new LogActivitySnapshot($"vm-{Number}.log", DateTimeOffset.UtcNow)));
        }
    }

    private sealed class BlockingLogActivitySource : ILogActivitySource
    {
        private readonly TaskCompletionSource<LogActivityReadResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<LogActivityReadResult> ReadLatestAsync(CancellationToken cancellationToken) =>
            new(_completion.Task);

        public void Release() => _completion.TrySetResult(LogActivityReadResult.NoLog());
    }

    private sealed class ConstantTimestampSource(DateTimeOffset timestamp) : ILogActivitySource
    {
        public ValueTask<LogActivityReadResult> ReadLatestAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(LogActivityReadResult.Available(
                new LogActivitySnapshot("activity.log", timestamp)));
        }
    }
}
