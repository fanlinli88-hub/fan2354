# Eight-VM Concurrent Monitoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run as many as eight virtual-machine monitors concurrently with independent caches, timeouts, alert counters, and stop controls so one unavailable VM cannot affect the other seven.

**Architecture:** Preserve `SingleVmMonitor` as the state-owning unit and add a 10-second per-check timeout at that boundary. Add a `MultiVmMonitor` coordinator that owns up to eight distinct `SingleVmMonitor` instances and performs only registration, result tagging, and concurrent lifecycle operations; it never shares log sources or state machines.

**Tech Stack:** C# 12, .NET 8, `Task`/`CancellationToken`, `TimeProvider`, xUnit, PowerShell build tooling.

---

## File Map

- Modify `src/WowVmMonitor.Core/Monitoring/SingleVmMonitor.cs`: enforce a per-check timeout while preserving caller cancellation and existing state transitions.
- Modify `src/WowVmMonitor.Core/Monitoring/VmMonitorStatus.cs`: add an explicit unexpected-error status so one failed source does not terminate its loop.
- Create `src/WowVmMonitor.Core/Monitoring/MonitoredVm.cs`: bind one stable machine identity to one distinct `SingleVmMonitor`.
- Create `src/WowVmMonitor.Core/Monitoring/MultiVmMonitorResult.cs`: tag each single-machine result with machine identity.
- Create `src/WowVmMonitor.Core/Monitoring/MultiVmMonitor.cs`: validate the eight-machine boundary and coordinate independent start/stop operations.
- Modify `tests/WowVmMonitor.Tests/Core/SingleVmMonitorTests.cs`: prove timeout, caller cancellation, and unexpected-source failure behavior.
- Create `tests/WowVmMonitor.Tests/Core/MultiVmMonitorTests.cs`: prove capacity, concurrency, state isolation, timeout isolation, and per-machine stop control.
- Create `tests/WowVmMonitor.Tests/Core/MultiVmMonitorResourceTests.cs`: guard against busy loops and unbounded retained memory with eight monitors.
- Modify `README.md`: record the eight-machine orchestration and default limits.

### Task 1: Add A Strong Per-Check Timeout

**Files:**
- Modify: `tests/WowVmMonitor.Tests/Core/SingleVmMonitorTests.cs`
- Modify: `src/WowVmMonitor.Core/Monitoring/SingleVmMonitor.cs`

- [ ] **Step 1: Write the failing timeout test**

Add this test and source double to `SingleVmMonitorTests`:

```csharp
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

private sealed class BlockingLogActivitySource : ILogActivitySource
{
    private readonly TaskCompletionSource<LogActivityReadResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask<LogActivityReadResult> ReadLatestAsync(CancellationToken cancellationToken) =>
        new(_completion.Task);

    public void Release() => _completion.TrySetResult(LogActivityReadResult.NoLog());
}
```

Change the existing test helper signature to pass the timeout:

```csharp
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
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj --filter "FullyQualifiedName~CheckTimeoutReturnsShareUnavailableWithoutWaitingForSource"
```

Expected: compilation fails because `SingleVmMonitor` has no `checkTimeout` parameter.

- [ ] **Step 3: Implement the minimal timeout behavior**

In `SingleVmMonitor`, add a field, validate the constructor argument, and use `Task.WaitAsync` so timeout does not depend on a blocked source honoring cancellation:

```csharp
private readonly TimeSpan _checkTimeout;

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
```

Replace the first read in `CheckAsync` with:

```csharp
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
```

- [ ] **Step 4: Run the focused and existing single-monitor tests**

Run:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj --filter "FullyQualifiedName~SingleVmMonitorTests"
```

Expected: every `SingleVmMonitorTests` case passes, including caller cancellation and the new timeout case.

- [ ] **Step 5: Commit the timeout increment**

```powershell
& 'C:\Program Files\Git\cmd\git.exe' add src/WowVmMonitor.Core/Monitoring/SingleVmMonitor.cs tests/WowVmMonitor.Tests/Core/SingleVmMonitorTests.cs
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: add per-vm check timeout"
```

### Task 2: Define Machine Identity And The Eight-Machine Boundary

**Files:**
- Create: `src/WowVmMonitor.Core/Monitoring/MonitoredVm.cs`
- Create: `src/WowVmMonitor.Core/Monitoring/MultiVmMonitorResult.cs`
- Create: `src/WowVmMonitor.Core/Monitoring/MultiVmMonitor.cs`
- Create: `tests/WowVmMonitor.Tests/Core/MultiVmMonitorTests.cs`

- [ ] **Step 1: Write failing capacity and identity tests**

Create `MultiVmMonitorTests.cs` with these first tests and helpers:

```csharp
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

    private sealed class ConstantSource(int number) : ILogActivitySource
    {
        public ValueTask<LogActivityReadResult> ReadLatestAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(LogActivityReadResult.Available(
                new LogActivitySnapshot($"vm-{number}.log", DateTimeOffset.UtcNow)));
    }
}
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj --filter "FullyQualifiedName~MultiVmMonitorTests"
```

Expected: compilation fails because `MonitoredVm` and `MultiVmMonitor` do not exist.

- [ ] **Step 3: Add the identity and tagged-result records**

Create `MonitoredVm.cs`:

```csharp
namespace WowVmMonitor.Core.Monitoring;

public sealed record MonitoredVm
{
    public MonitoredVm(string id, string displayName, SingleVmMonitor monitor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(monitor);
        Id = id;
        DisplayName = displayName;
        Monitor = monitor;
    }

    public string Id { get; }
    public string DisplayName { get; init; }
    public SingleVmMonitor Monitor { get; }
}
```

Create `MultiVmMonitorResult.cs`:

```csharp
namespace WowVmMonitor.Core.Monitoring;

public sealed record MultiVmMonitorResult(
    string MachineId,
    string DisplayName,
    SingleVmMonitorResult Result);
```

- [ ] **Step 4: Add minimal coordinator validation**

Create `MultiVmMonitor.cs`:

```csharp
namespace WowVmMonitor.Core.Monitoring;

public sealed class MultiVmMonitor
{
    public const int MaximumMachineCount = 8;
    private readonly IReadOnlyDictionary<string, MonitoredVm> _machines;

    public MultiVmMonitor(IEnumerable<MonitoredVm> machines)
    {
        ArgumentNullException.ThrowIfNull(machines);
        var materialized = machines.ToArray();
        if (materialized.Length > MaximumMachineCount)
        {
            throw new ArgumentOutOfRangeException(nameof(machines));
        }

        if (materialized.Select(machine => machine.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != materialized.Length)
        {
            throw new ArgumentException("Machine identifiers must be unique.", nameof(machines));
        }

        if (materialized.Select(machine => machine.Monitor).Distinct(ReferenceEqualityComparer.Instance).Count() != materialized.Length)
        {
            throw new ArgumentException("Each machine must own a distinct monitor instance.", nameof(machines));
        }

        _machines = materialized.ToDictionary(machine => machine.Id, StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _machines.Count;
}
```

- [ ] **Step 5: Run focused tests and commit**

Run:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj --filter "FullyQualifiedName~MultiVmMonitorTests"
```

Expected: all three tests pass.

Commit:

```powershell
& 'C:\Program Files\Git\cmd\git.exe' add src/WowVmMonitor.Core/Monitoring/MonitoredVm.cs src/WowVmMonitor.Core/Monitoring/MultiVmMonitorResult.cs src/WowVmMonitor.Core/Monitoring/MultiVmMonitor.cs tests/WowVmMonitor.Tests/Core/MultiVmMonitorTests.cs
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: define eight-vm monitor coordinator"
```

### Task 3: Add Concurrent Start And Independent Stop Control

**Files:**
- Modify: `src/WowVmMonitor.Core/Monitoring/MultiVmMonitor.cs`
- Modify: `tests/WowVmMonitor.Tests/Core/MultiVmMonitorTests.cs`

- [ ] **Step 1: Write failing concurrent lifecycle tests**

Add tests using eight `CountingSource` instances and a thread-safe result collection:

```csharp
[Fact]
public async Task StartsEightMachinesAndTagsTheirResults()
{
    var sources = Enumerable.Range(1, 8).Select(number => new CountingSource(number)).ToArray();
    var monitor = new MultiVmMonitor(sources.Select(CreateMachine));
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
}

[Fact]
public async Task StoppingOneMachineLeavesOtherSevenRunning()
{
    var sources = Enumerable.Range(1, 8).Select(number => new CountingSource(number)).ToArray();
    var monitor = new MultiVmMonitor(sources.Select(source => CreateMachine(source, TimeSpan.FromMilliseconds(10))));

    await monitor.StartAllAsync(_ => ValueTask.CompletedTask);
    await WaitUntilAsync(() => sources.All(source => source.ReadCount >= 1));
    await monitor.StopAsync("vm-1");
    var stoppedCount = sources[0].ReadCount;
    var otherCount = sources[1].ReadCount;
    await WaitUntilAsync(() => sources[1].ReadCount > otherCount);
    await monitor.StopAllAsync();

    Assert.False(monitor.IsRunning("vm-1"));
    Assert.Equal(stoppedCount, sources[0].ReadCount);
    Assert.All(Enumerable.Range(2, 7), number => Assert.True(sources[number - 1].ReadCount > otherCount));
}
```

Add deterministic helpers:

```csharp
private static async Task WaitUntilAsync(Func<bool> condition)
{
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
    while (!condition() && DateTime.UtcNow < deadline)
    {
        await Task.Delay(5);
    }

    Assert.True(condition(), "Condition was not reached before the test deadline.");
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

private static MonitoredVm CreateMachine(CountingSource source, TimeSpan interval) =>
    CreateMachine($"vm-{source.Number}", source, interval, TimeSpan.FromSeconds(10));

private static MonitoredVm CreateMachine(
    string id,
    ILogActivitySource source,
    TimeSpan interval,
    TimeSpan checkTimeout)
{
    var stateMachine = new LogMonitorStateMachine(
        new MonitorThresholds(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)),
        requiredAlertObservations: 2,
        requiredRecoveryObservations: 2);
    return new MonitoredVm(
        id,
        id.ToUpperInvariant(),
        new SingleVmMonitor(source, stateMachine, interval, checkTimeout: checkTimeout));
}
```

- [ ] **Step 2: Run and verify RED**

Run the `MultiVmMonitorTests` filter. Expected: compilation fails because lifecycle methods do not exist.

- [ ] **Step 3: Implement lifecycle operations**

Add to `MultiVmMonitor`:

```csharp
public bool IsRunning(string machineId) => GetMachine(machineId).Monitor.IsRunning;

public Task StartAsync(
    string machineId,
    Func<MultiVmMonitorResult, ValueTask> onResult,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(onResult);
    var machine = GetMachine(machineId);
    return machine.Monitor.StartAsync(
        result => onResult(new MultiVmMonitorResult(machine.Id, machine.DisplayName, result)),
        cancellationToken);
}

public Task StartAllAsync(
    Func<MultiVmMonitorResult, ValueTask> onResult,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(onResult);
    return Task.WhenAll(_machines.Values.Select(machine =>
        StartAsync(machine.Id, onResult, cancellationToken)));
}

public Task StopAsync(string machineId) => GetMachine(machineId).Monitor.StopAsync();

public async Task StopAllAsync()
{
    var stopTasks = _machines.Values.Select(machine => machine.Monitor.StopAsync()).ToArray();
    await Task.WhenAll(stopTasks).ConfigureAwait(false);
}

private MonitoredVm GetMachine(string machineId)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(machineId);
    return _machines.TryGetValue(machineId, out var machine)
        ? machine
        : throw new KeyNotFoundException($"Machine '{machineId}' is not registered.");
}
```

- [ ] **Step 4: Run focused tests and commit**

Run all `MultiVmMonitorTests`; expected: capacity, start, result tagging, and independent stop tests pass.

Commit:

```powershell
& 'C:\Program Files\Git\cmd\git.exe' add src/WowVmMonitor.Core/Monitoring/MultiVmMonitor.cs tests/WowVmMonitor.Tests/Core/MultiVmMonitorTests.cs
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: coordinate independent vm lifecycles"
```

### Task 4: Prove Timeout, State, And Failure Isolation

**Files:**
- Modify: `src/WowVmMonitor.Core/Monitoring/VmMonitorStatus.cs`
- Modify: `src/WowVmMonitor.Core/Monitoring/SingleVmMonitor.cs`
- Modify: `tests/WowVmMonitor.Tests/Core/SingleVmMonitorTests.cs`
- Modify: `tests/WowVmMonitor.Tests/Core/MultiVmMonitorTests.cs`

- [ ] **Step 1: Write failing unexpected-error test**

Add to `SingleVmMonitorTests`:

```csharp
[Fact]
public async Task UnexpectedSourceFailureReturnsErrorWithoutChangingAlertState()
{
    var source = new ThrowingSource(new InvalidOperationException("source failed"));
    var monitor = CreateMonitor(source);

    var result = await monitor.CheckAsync(Now);

    Assert.Equal(VmMonitorStatus.Error, result.Status);
    Assert.Equal("source failed", result.ErrorMessage);
    Assert.False(result.IsAlerting);
}

private sealed class ThrowingSource(Exception exception) : ILogActivitySource
{
    public ValueTask<LogActivityReadResult> ReadLatestAsync(CancellationToken cancellationToken) =>
        ValueTask.FromException<LogActivityReadResult>(exception);
}
```

- [ ] **Step 2: Run and verify RED**

Run the focused test. Expected: compilation fails because `VmMonitorStatus.Error` does not exist.

- [ ] **Step 3: Add machine-scoped unexpected-error handling**

Add `Error` to `VmMonitorStatus`. In `SingleVmMonitor.CheckAsync`, preserve `OperationCanceledException` for explicit lifecycle cancellation, but convert other exceptions around the source read into:

```csharp
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
```

- [ ] **Step 4: Write failing eight-machine isolation tests**

Add to `MultiVmMonitorTests`:

```csharp
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
    var staleSource = new ConstantTimestampSource(DateTimeOffset.UtcNow.AddMinutes(-10));
    var activeSource = new ConstantTimestampSource(DateTimeOffset.UtcNow);
    var stale = CreateMachine(
        "stale",
        staleSource,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromSeconds(1));
    var active = CreateMachine(
        "active",
        activeSource,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromSeconds(1));

    var firstStale = await stale.Monitor.CheckAsync(DateTimeOffset.UtcNow);
    var activeResult = await active.Monitor.CheckAsync(DateTimeOffset.UtcNow);
    var secondStale = await stale.Monitor.CheckAsync(DateTimeOffset.UtcNow);

    Assert.False(firstStale.IsAlerting);
    Assert.False(activeResult.IsAlerting);
    Assert.True(secondStale.IsAlerting);
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
```

- [ ] **Step 5: Run isolation tests and commit**

Run all core tests; expected: one timed-out machine reports unavailable, seven report normal, and state counters remain independent.

Commit:

```powershell
& 'C:\Program Files\Git\cmd\git.exe' add src/WowVmMonitor.Core/Monitoring/VmMonitorStatus.cs src/WowVmMonitor.Core/Monitoring/SingleVmMonitor.cs tests/WowVmMonitor.Tests/Core/SingleVmMonitorTests.cs tests/WowVmMonitor.Tests/Core/MultiVmMonitorTests.cs
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: isolate vm failures and state"
```

### Task 5: Add Resource Guards And Documentation

**Files:**
- Create: `tests/WowVmMonitor.Tests/Core/MultiVmMonitorResourceTests.cs`
- Modify: `README.md`

- [ ] **Step 1: Write the eight-monitor resource test**

Create `MultiVmMonitorResourceTests.cs`:

```csharp
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

        await monitor.StartAllAsync(_ => ValueTask.CompletedTask);
        await Task.Delay(200);
        await monitor.StopAllAsync();

        Assert.All(sources, source => Assert.Equal(1, source.ReadCount));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memoryBefore = GC.GetTotalMemory(true);

        for (var cycle = 0; cycle < 250; cycle++)
        {
            await monitor.StartAllAsync(_ => ValueTask.CompletedTask);
            await Task.Delay(1);
            await monitor.StopAllAsync();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var retainedGrowth = GC.GetTotalMemory(true) - memoryBefore;

        Assert.True(retainedGrowth < 4 * 1024 * 1024,
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
```

- [ ] **Step 2: Run the resource test**

Run:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj --filter "FullyQualifiedName~MultiVmMonitorResourceTests"
```

Expected: every source is read exactly once during the idle window and retained growth stays below 4 MB across repeated eight-monitor lifecycle cycles.

- [ ] **Step 3: Update README with confirmed limits**

Append to the implemented modules section:

```markdown
- Concurrent orchestration for up to eight independently cached and timed virtual machines.
- A 10-second default per-machine check timeout with isolated start and stop control.

## Runtime limits

- Default check interval: 60 seconds per enabled machine.
- Default per-check timeout: 10 seconds per machine.
- Maximum concurrent machines: 8.
- Monitoring reads directory metadata only; log contents are never read.
```

- [ ] **Step 4: Run complete Release verification**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-and-test.ps1
```

Expected: Release build succeeds with zero warnings and zero errors, signing succeeds, and all tests pass with zero failures.

- [ ] **Step 5: Perform resource acceptance sampling**

Run the Release tests while observing the `testhost` process:

```powershell
$before = Get-Process testhost -ErrorAction SilentlyContinue
powershell -ExecutionPolicy Bypass -File .\build-and-test.ps1
Get-Process testhost -ErrorAction SilentlyContinue | Select-Object CPU,WorkingSet64,PrivateMemorySize64
```

Expected: automated resource guards pass. Record that final `<5%` CPU and `<150 MB` stable-memory acceptance must also be repeated against eight real UNC shares when those endpoints are available, because test doubles cannot measure real network-server behavior.

- [ ] **Step 6: Commit resource guards and docs**

```powershell
& 'C:\Program Files\Git\cmd\git.exe' add tests/WowVmMonitor.Tests/Core/MultiVmMonitorResourceTests.cs README.md
& 'C:\Program Files\Git\cmd\git.exe' commit -m "test: verify eight-vm resource bounds"
```

## Final Verification Checklist

- [ ] Confirm no source or state-machine instance is shared between registrations.
- [ ] Confirm a ninth registration is rejected.
- [ ] Confirm each check has its own 10-second timeout boundary.
- [ ] Confirm stopping one machine leaves seven running.
- [ ] Confirm one blocked, unavailable, or faulting source does not stop other machines.
- [ ] Confirm aggregate stop cancels all monitor loops without serial timeout waits.
- [ ] Confirm full Release build reports zero warnings and zero errors.
- [ ] Confirm all automated tests report zero failures.
- [ ] Record real eight-UNC-share CPU, memory, and network observations before production acceptance.
