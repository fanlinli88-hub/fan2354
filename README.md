# WowVmMonitor

Professional C#/.NET implementation of the shared-log monitor.

## Projects

- `WowVmMonitor.Core`: domain rules and monitoring state.
- `WowVmMonitor.Infrastructure`: file shares, notifications, credentials, and persistence.
- `WowVmMonitor.App`: application entry point. No UI is implemented yet.
- `WowVmMonitor.Tests`: automated tests.

## Build and test

```powershell
powershell -ExecutionPolicy Bypass -File .\build-and-test.ps1
```

The build script compiles the solution, signs local outputs with the trusted
`WowVmMonitor Local Development` certificate, and then runs the test suite.
The certificate private key stays in the current user's Windows certificate
store and is not committed to Git.

The existing PowerShell monitor remains separate and unchanged.

## Implemented modules

- Core log-age classification with 5-minute warning and 10-minute alert thresholds.
- Stateful two-observation alert and recovery transitions.
- Shallow latest-log discovery across character and date directories.
- Cached log selection with explicit refresh and missing-file fallback.
- A cancellable single-VM monitor that combines log discovery with state transitions.
- An asynchronous shared-log source that converts share failures into explicit results.
- Concurrent orchestration for up to eight independently cached and timed virtual machines.
- A 10-second default per-machine check timeout with isolated start and stop control.

## Runtime limits

- Default check interval: 60 seconds per enabled machine.
- Default per-check timeout: 10 seconds per machine.
- Maximum concurrent machines: 8.
- Monitoring reads directory metadata only; log contents are never read.

## Configuration and credentials

- Configuration: `%LocalAppData%\WowVmMonitor\config.json`
- Backup: `%LocalAppData%\WowVmMonitor\config.json.bak`
- Credentials: Windows Credential Manager targets named `WowVmMonitor/share/<machine-id>`
- Usernames and passwords are never stored in JSON or emitted in diagnostics.
- A valid backup is restored automatically. If both files are invalid, monitoring remains disabled until the generated default is reviewed.
- Starting WowVmMonitor after a Windows restart reloads saved credentials and reconnects enabled shares; the app does not register itself for automatic startup.
