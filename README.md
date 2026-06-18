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
