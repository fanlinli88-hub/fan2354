# Ntfy Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Send isolated ntfy notifications for alert and recovery transitions through topic `wow-vm-85898-fan2354`, with UI configuration and test delivery.

**Architecture:** Upgrade configuration to schema v2 with ntfy settings, place notification contracts in App, and implement HTTP plus a bounded background dispatcher in Infrastructure. MonitoringController only performs non-blocking enqueue on state transitions; the WPF settings page invokes the same client asynchronously for test messages.

**Tech Stack:** C# 12, .NET 8, `HttpClient`, `System.Threading.Channels`, WPF/MVVM, xUnit, Inno Setup release pipeline

---

### Task 1: Version 2 Ntfy Configuration

**Files:**
- Create: `src/WowVmMonitor.Core/Configuration/NtfyConfiguration.cs`
- Modify: `src/WowVmMonitor.Core/Configuration/MonitorConfiguration.cs`
- Modify: `src/WowVmMonitor.Core/Configuration/ConfigurationValidator.cs`
- Modify: `src/WowVmMonitor.Infrastructure/Configuration/ConfigurationMigrator.cs`
- Modify: `src/WowVmMonitor.Infrastructure/Configuration/ConfigurationStore.cs`
- Test: `tests/WowVmMonitor.Tests/Core/ConfigurationValidatorTests.cs`
- Test: `tests/WowVmMonitor.Tests/Infrastructure/ConfigurationMigratorTests.cs`
- Test: `tests/WowVmMonitor.Tests/Infrastructure/ConfigurationStoreTests.cs`

- [ ] **Step 1: Write failing schema v2 migration and validation tests**

Require current schema version 2, migrate an existing schema v1 document without losing machines, default to disabled topic `wow-vm-85898-fan2354`, and reject empty/space/over-64-character/invalid-character topics.

```csharp
[Fact]
public void MigratesVersionOneToVersionTwoWithDefaultNtfy()
{
    const string json = """{ "schemaVersion": 1, "monitoring": { "checkIntervalSeconds": 60, "checkTimeoutSeconds": 10, "warningAfterSeconds": 300, "alertAfterSeconds": 600 }, "machines": [] }""";
    var result = new ConfigurationMigrator().DeserializeAndMigrate(json);
    Assert.True(result.WasMigrated);
    Assert.Equal(2, result.Configuration.SchemaVersion);
    Assert.False(result.Configuration.Ntfy.Enabled);
    Assert.Equal("wow-vm-85898-fan2354", result.Configuration.Ntfy.Topic);
}
```

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~ConfigurationValidatorTests|FullyQualifiedName~ConfigurationMigratorTests|FullyQualifiedName~ConfigurationStoreTests"
```

Expected: FAIL because schema v2 and `NtfyConfiguration` do not exist.

- [ ] **Step 3: Implement schema v2 with source-compatible construction**

Create:

```csharp
public sealed record NtfyConfiguration(bool Enabled, string Topic)
{
    public const string DefaultTopic = "wow-vm-85898-fan2354";
    public static NtfyConfiguration CreateDefault() => new(false, DefaultTopic);
}
```

Make schema version 2 and add `Ntfy` to `MonitorConfiguration`. Preserve the existing three-argument constructor by delegating to the four-argument constructor with `CreateDefault()`, so unrelated tests and callers remain valid.

- [ ] **Step 4: Implement sequential migration and validation**

Handle version 0, version 1, and version 2 explicitly. Version 1 deserializes its monitoring/machine fields and produces a version 2 configuration with default ntfy. Update migration messages to state the actual source and target version. Validate topic with `^[A-Za-z0-9_-]{1,64}$` even when disabled.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~ConfigurationValidatorTests|FullyQualifiedName~ConfigurationMigratorTests|FullyQualifiedName~ConfigurationStoreTests"
git add src/WowVmMonitor.Core/Configuration src/WowVmMonitor.Infrastructure/Configuration tests/WowVmMonitor.Tests
git commit -m "feat: add versioned ntfy configuration"
```

### Task 2: Retrying Ntfy HTTP Client

**Files:**
- Create: `src/WowVmMonitor.App/Notifications/NtfySendResult.cs`
- Create: `src/WowVmMonitor.App/Notifications/INtfyTestService.cs`
- Create: `src/WowVmMonitor.App/Notifications/INtfySender.cs`
- Create: `src/WowVmMonitor.App/Notifications/NullNtfyTestService.cs`
- Create: `src/WowVmMonitor.App/Notifications/NtfyNotification.cs`
- Create: `src/WowVmMonitor.Infrastructure/Notifications/NtfyClient.cs`
- Test: `tests/WowVmMonitor.Tests/Infrastructure/NtfyClientTests.cs`

- [ ] **Step 1: Write failing HTTP behavior tests**

Use a recording `HttpMessageHandler` to assert JSON POSTs target `https://ntfy.sh`, contain the requested topic, machine-safe message text, title, priority and tag. Add tests for first-attempt failure then success, two failures returning a stable result, and timeout/cancellation.

```csharp
var result = await client.SendTestAsync("wow-vm-85898-fan2354", CancellationToken.None);
Assert.True(result.Succeeded);
Assert.Equal(2, handler.RequestCount);
Assert.DoesNotContain("password", handler.LastBody, StringComparison.OrdinalIgnoreCase);
```

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~NtfyClientTests"
```

Expected: FAIL because notification contracts and `NtfyClient` do not exist.

- [ ] **Step 3: Implement structured ntfy JSON delivery**

`NtfyClient` implements both `INtfySender` and `INtfyTestService`, owns one injected `HttpClient`, posts JSON to `https://ntfy.sh`, and uses `WaitAsync` with an injected 10-second timeout. Retry once after an injected delay. Convert HTTP, timeout, network and cancellation outcomes into `NtfySendResult`; rethrow only caller cancellation. `NullNtfyTestService` returns a stable unavailable result for compatibility construction and performs no network work.

Test notification title is `WowVmMonitor 测试通知`. Alert uses title `WowVmMonitor 异常告警`, priority 5 and `warning`; recovery uses title `WowVmMonitor 恢复正常`, priority 3 and `white_check_mark`.

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~NtfyClientTests"
git add src/WowVmMonitor.App/Notifications src/WowVmMonitor.Infrastructure/Notifications tests/WowVmMonitor.Tests/Infrastructure/NtfyClientTests.cs
git commit -m "feat: add retrying ntfy client"
```

### Task 3: Bounded Background Notification Dispatcher

**Files:**
- Create: `src/WowVmMonitor.App/Notifications/INotificationDispatcher.cs`
- Create: `src/WowVmMonitor.App/Notifications/NullNotificationDispatcher.cs`
- Create: `src/WowVmMonitor.Infrastructure/Notifications/NtfyNotificationDispatcher.cs`
- Test: `tests/WowVmMonitor.Tests/Infrastructure/NtfyNotificationDispatcherTests.cs`

- [ ] **Step 1: Write failing queue isolation tests**

Test that enqueue returns immediately while the client is blocked, configuration disables/enables delivery, a full capacity queue evicts the oldest item and increments `DroppedCount`, ordering is preserved, and `DisposeAsync` completes within a bounded cancellation window.

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~NtfyNotificationDispatcherTests"
```

Expected: FAIL because dispatcher types do not exist.

- [ ] **Step 3: Implement channel worker and lifecycle**

Use a capacity-100 `Channel<NtfyNotification>`, explicit oldest-item removal when full, a single background consumer, thread-safe configuration snapshot, `DroppedCount`, and `LastFailureCode`. `TryEnqueue` performs no await or network work. `DisposeAsync` completes the writer, waits up to five seconds, then cancels the worker.

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~NtfyNotificationDispatcherTests"
git add src/WowVmMonitor.App/Notifications src/WowVmMonitor.Infrastructure/Notifications tests/WowVmMonitor.Tests/Infrastructure/NtfyNotificationDispatcherTests.cs
git commit -m "feat: isolate ntfy delivery in background queue"
```

### Task 4: Alert And Recovery Integration

**Files:**
- Create: `src/WowVmMonitor.App/Notifications/MonitorNotificationFactory.cs`
- Modify: `src/WowVmMonitor.Infrastructure/DesktopServices/MonitoringController.cs`
- Test: `tests/WowVmMonitor.Tests/App/MonitorNotificationFactoryTests.cs`

- [ ] **Step 1: Write failing transition-only notification tests**

Pass real `MultiVmMonitorResult` values to the pure `MonitorNotificationFactory`. Assert alert and recovery each create one message. Normal, warning, share unavailable, no-log and error results return null. Assert notification body contains display name, local time and optional age, but not share/log paths.

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~MonitorNotificationFactoryTests"
```

Expected: FAIL because `MonitorNotificationFactory` does not exist.

- [ ] **Step 3: Integrate non-blocking enqueue**

Implement `MonitorNotificationFactory.Create(MultiVmMonitorResult, DateTimeOffset)` and return null for `MonitorTransition.None`. Accept `INotificationDispatcher` in MonitoringController, defaulting to `NullNotificationDispatcher` for existing callers. On `StartAsync`, configure it from the freshly loaded `configuration.Ntfy`. In `Publish`, call the factory and enqueue non-null results after recording history. Do not await the dispatcher.

- [ ] **Step 4: Run monitoring isolation tests and commit**

```powershell
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~MonitorNotificationFactoryTests|FullyQualifiedName~MultiVmMonitorTests|FullyQualifiedName~MultiVmMonitorResourceTests"
git add src/WowVmMonitor.App/Notifications src/WowVmMonitor.Infrastructure/DesktopServices tests/WowVmMonitor.Tests/App
git commit -m "feat: notify on monitor alert transitions"
```

### Task 5: Settings UI, Composition, Real Delivery, And Release

**Files:**
- Modify: `src/WowVmMonitor.App/Settings/SettingsViewModel.cs`
- Modify: `src/WowVmMonitor.Desktop/Views/SettingsView.xaml`
- Modify: `src/WowVmMonitor.Desktop/DesktopCompositionRoot.cs`
- Modify: `src/WowVmMonitor.Desktop/Lifetime/DesktopLifetimeController.cs`
- Modify: `tests/WowVmMonitor.Tests/App/SettingsViewModelTests.cs`
- Modify: `tests/WowVmMonitor.Desktop.Tests/WpfSmokeTests.cs`
- Modify: `tests/WowVmMonitor.Desktop.Tests/DesktopLifetimeControllerTests.cs`
- Create: `tests/WowVmMonitor.Tests/Infrastructure/NtfyLiveTests.cs`
- Modify: `README.md`

- [ ] **Step 1: Write failing settings and WPF tests**

Require settings load/save to preserve enabled/topic, invalid topics to block save and testing, asynchronous test success/failure text, and WPF labels `启用 ntfy`, `ntfy 主题`, `测试推送`.

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~SettingsViewModelTests"
dotnet test .\tests\WowVmMonitor.Desktop.Tests\WowVmMonitor.Desktop.Tests.csproj -c Release --filter "FullyQualifiedName~WpfSmokeTests"
```

- [ ] **Step 3: Implement settings and composition**

Inject `INtfyTestService` into SettingsViewModel with a no-network default for compatibility. Add async `TestNtfyCommand`, topic validation, stable Chinese results, and ntfy fields in saved schema v2 configuration. Add the three Chinese controls to SettingsView.

Composition creates one `HttpClient`, `NtfyClient`, and `NtfyNotificationDispatcher`; shares them between settings test and MonitoringController. Desktop exit stops monitoring first, then gives dispatcher up to five seconds to drain before application shutdown. Dispose all owned resources exactly once.

- [ ] **Step 4: Run complete automated verification**

```powershell
dotnet build .\WowVmMonitor.sln -c Release --no-restore
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --no-build
dotnet test .\tests\WowVmMonitor.Desktop.Tests\WowVmMonitor.Desktop.Tests.csproj -c Release --no-build
git diff --check
```

- [ ] **Step 5: Send one authorized real test notification**

Add `NtfyLiveTests`, which returns without network access unless `WOWVMMONITOR_NTFY_LIVE_TOPIC` is set. Run exactly once with:

```powershell
$env:WOWVMMONITOR_NTFY_LIVE_TOPIC='wow-vm-85898-fan2354'
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~NtfyLiveTests"
Remove-Item Env:WOWVMMONITOR_NTFY_LIVE_TOPIC
```

The test calls `NtfyClient.SendTestAsync`, requires HTTP success, and sends one `WowVmMonitor 测试通知`. Ask the user to confirm receipt in the Android client. Do not send alert/recovery test messages repeatedly.

- [ ] **Step 6: Upgrade the active configuration and rebuild package 1.1.0**

Load `%LocalAppData%\WowVmMonitor\config.json` through `ConfigurationStore` so schema v1 is atomically migrated to v2. Keep ntfy disabled until the user enables it in UI. Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\release\Build-Release.ps1 -Version 1.1.0
```

Verify application and installer versions/signatures and update README with ntfy setup instructions.

- [ ] **Step 7: Commit and push**

```powershell
git add src tests README.md
git commit -m "feat: add configurable ntfy notifications"
git push origin codex/wpf-ui
```
