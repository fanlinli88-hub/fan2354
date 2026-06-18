# Eight-VM Concurrent Monitoring Design

## Goal

Extend WowVmMonitor from one independently monitored virtual machine to as many as eight concurrent virtual machines. A failure, timeout, cancellation, or stop operation for one machine must not delay or alter the other seven machines.

## Scope

This change adds orchestration around the existing `SingleVmMonitor`. It does not rewrite the log locator, shared-log activity source, or state machine. Notification delivery, history persistence, configuration UI, and system-tray behavior remain outside this increment.

## Runtime Limits

- At most eight virtual machines may be registered concurrently.
- Each enabled machine checks every 60 seconds by default.
- Each individual check has a 10-second timeout by default.
- With eight idle monitors, average process CPU usage must remain below 5 percent.
- Stable process memory must remain below 150 MB.
- Monitoring reads directory metadata and the latest log modification time only. It never reads log contents.

## Architecture

Add a lightweight coordinator that owns a collection of independent monitoring units. Each unit wraps one existing `SingleVmMonitor` and contains all mutable state associated with one virtual machine.

The coordinator is responsible only for registration, lookup, aggregate start and stop operations, and forwarding per-machine results. It must not own shared alert counters, shared log caches, or one global check timeout.

Each monitoring unit owns:

- a unique machine identifier and display name;
- one `SingleVmMonitor`;
- one `SharedLogActivitySource`, including its private cached log path and snapshot;
- one `LogMonitorStateMachine`, including its private alert and recovery counters;
- one lifecycle cancellation source;
- one 10-second timeout source per check;
- one result callback associated with that machine.

Construction must ensure that activity sources and state machines are never reused between machine registrations.

## Concurrency Model

Each machine runs its existing periodic monitoring loop independently. Aggregate start launches all enabled machines without serially waiting for a check to finish. Aggregate stop requests cancellation for every machine first, then awaits all monitor tasks concurrently.

The coordinator supports:

- starting all registered machines;
- stopping all registered machines;
- starting one machine;
- stopping one machine;
- restarting a previously stopped machine;
- querying whether an individual machine is running.

Starting a machine that is already running must fail with a clear `InvalidOperationException`. Stopping a stopped machine is idempotent. Registration of a ninth machine must be rejected before any monitoring task starts.

## Timeout And Failure Isolation

Every check links the machine lifecycle token with a new 10-second timeout token. Timeout applies only to that machine and that check. A timed-out check produces a machine-scoped `ShareUnavailable` result and does not cancel the machine's future checks.

Expected share failures include missing paths, powered-off virtual machines, network failures, access denial, and invalid credentials. These failures preserve the machine's last valid alert state and do not change any other machine.

An unexpected exception from one machine's check or callback is caught at the machine boundary and reported as a machine-scoped failure. It must not terminate aggregate coordination or another machine's loop.

Explicit lifecycle cancellation is distinct from timeout. Stopping one machine ends only that machine's loop. Aggregate stop cancels all eight lifecycle sources before awaiting completion so one slow machine cannot impose sequential delay.

## Data Flow

1. The coordinator receives up to eight machine registrations.
2. Each registration creates its own log source, state machine, and single-machine monitor.
3. Starting monitoring launches the registered machine loops concurrently.
4. A machine begins a check with its own linked 10-second timeout.
5. The machine reads only its own shared-log metadata and updates only its own state machine.
6. The result callback receives the machine identity together with the check result.
7. Failure, cancellation, or timeout is handled at the same machine boundary.
8. The other machine loops continue without waiting for or inheriting state from the failed machine.

## Test Strategy

Development follows test-first red-green-refactor cycles. Automated tests must cover:

- eight machines can be registered, started, and observed concurrently;
- a ninth registration is rejected;
- each machine uses a different activity source, cache, and state machine;
- one machine timing out does not prevent the other seven from completing their checks;
- one machine accumulating consecutive alerts does not change another machine's counters or alert state;
- stopping one machine leaves the other seven running and producing results;
- a timed-out or unavailable machine is checked again on its next interval and can recover;
- aggregate stop requests cancellation for all machines before awaiting completion;
- repeated start and stop cycles do not retain monitor tasks or cancellation sources;
- a high-frequency test run does not show unbounded retained-memory growth;
- the full existing suite remains green.

The timeout tests use controllable activity-source test doubles and short test timeouts rather than waiting 10 real seconds. Performance acceptance uses a Release build with eight monitors at the real 60-second interval. CPU is sampled over a representative idle monitoring window, and working-set memory is checked for stabilization below 150 MB.

## Acceptance Criteria

- Eight virtual machines run concurrently with independent caches, timeouts, alert counters, and stop controls.
- Powering off one virtual machine results only in that machine becoming unavailable.
- The other seven machines continue their scheduled checks and produce results.
- Average CPU usage remains below 5 percent during the defined eight-machine idle run.
- Stable process memory remains below 150 MB.
- Monitoring performs no log-content reads and generates only the metadata network traffic required for discovery and last-write-time checks.
- Release build completes with zero warnings and zero errors.
- All automated tests pass.
