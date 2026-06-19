# Chinese UI And Local Time Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Display all operational UI text in Simplified Chinese and render timestamps in the Windows local time zone without changing UTC monitoring calculations.

**Architecture:** Keep UTC `DateTimeOffset` values in Core and Infrastructure. Add presentation-only formatters and status mappings in App, expose formatted strings from view models, and bind WPF columns to those strings. Translate static WPF and tray labels directly at their presentation boundaries.

**Tech Stack:** C# 12, .NET 8, WPF, WinForms `NotifyIcon`, xUnit

---

### Task 1: Local Time Presentation

**Files:**
- Create: `src/WowVmMonitor.App/Presentation/LocalTimeText.cs`
- Modify: `src/WowVmMonitor.App/Monitoring/MachineStatusViewModel.cs`
- Modify: `src/WowVmMonitor.App/Monitoring/MonitoringDashboardViewModel.cs`
- Create: `src/WowVmMonitor.App/History/IncidentRecordViewModel.cs`
- Modify: `src/WowVmMonitor.App/History/IncidentHistoryViewModel.cs`
- Test: `tests/WowVmMonitor.Tests/App/LocalTimeTextTests.cs`

- [ ] **Step 1: Write the failing formatter tests**

Test a UTC value with a fixed China Standard Time zone and a null value:

```csharp
[Fact]
public void UtcTimestampIsFormattedInRequestedLocalTimeZone()
{
    var zone = TimeZoneInfo.CreateCustomTimeZone("CST", TimeSpan.FromHours(8), "CST", "CST");
    var value = new DateTimeOffset(2026, 6, 19, 3, 1, 28, TimeSpan.Zero);
    Assert.Equal("2026-06-19 11:01:28", LocalTimeText.Format(value, zone));
}

[Fact]
public void MissingTimestampUsesPlaceholder() =>
    Assert.Equal("--", LocalTimeText.Format(null, TimeZoneInfo.Utc));
```

- [ ] **Step 2: Run the tests and verify RED**

Run:

```powershell
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~LocalTimeTextTests"
```

Expected: FAIL because `LocalTimeText` does not exist.

- [ ] **Step 3: Implement the formatter and display properties**

Implement:

```csharp
public static class LocalTimeText
{
    public static string Format(DateTimeOffset? value) => Format(value, TimeZoneInfo.Local);

    public static string Format(DateTimeOffset? value, TimeZoneInfo timeZone) =>
        value is null
            ? "--"
            : TimeZoneInfo.ConvertTime(value.Value, timeZone).ToString("yyyy-MM-dd HH:mm:ss");
}
```

Add `LastWriteTimeText` to `MachineStatusViewModel`, raise it when `LastWriteTime` changes, add `LastCompletedCheckText` to `MonitoringDashboardViewModel`, and wrap history records with `IncidentRecordViewModel` exposing `TimestampText`.

- [ ] **Step 4: Bind all timestamp columns to formatted strings**

In `StatusView.xaml`, bind the modified column to `LastWriteTimeText` and the last-check label to `LastCompletedCheckText`. In `IncidentHistoryView.xaml`, bind time to `TimestampText`.

- [ ] **Step 5: Run formatter and existing view-model tests**

Run:

```powershell
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~LocalTimeTextTests|FullyQualifiedName~MonitoringDashboardViewModelTests|FullyQualifiedName~InMemoryIncidentHistoryTests"
```

Expected: PASS with zero failures.

- [ ] **Step 6: Commit**

```powershell
git add src/WowVmMonitor.App tests/WowVmMonitor.Tests/App src/WowVmMonitor.Desktop/Views
git commit -m "fix: display monitor timestamps in local time"
```

### Task 2: Chinese Monitor Status Mapping

**Files:**
- Create: `src/WowVmMonitor.App/Presentation/MonitorStatusText.cs`
- Modify: `src/WowVmMonitor.Infrastructure/DesktopServices/MonitoringController.cs`
- Test: `tests/WowVmMonitor.Tests/App/MonitorStatusTextTests.cs`

- [ ] **Step 1: Write the failing status mapping test**

```csharp
[Theory]
[InlineData(VmMonitorStatus.Normal, "正常")]
[InlineData(VmMonitorStatus.Warning, "预警")]
[InlineData(VmMonitorStatus.Alert, "异常")]
[InlineData(VmMonitorStatus.Recovery, "已恢复")]
[InlineData(VmMonitorStatus.ShareUnavailable, "共享不可访问")]
[InlineData(VmMonitorStatus.NoLog, "未找到日志")]
[InlineData(VmMonitorStatus.Error, "检测错误")]
public void EveryMonitorStatusHasChineseText(VmMonitorStatus status, string expected) =>
    Assert.Equal(expected, MonitorStatusText.Format(status));
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~MonitorStatusTextTests"
```

Expected: FAIL because `MonitorStatusText` does not exist.

- [ ] **Step 3: Implement exhaustive mapping**

Use a switch expression over every `VmMonitorStatus` value and throw `ArgumentOutOfRangeException` for unknown values. Replace `value.Status.ToString()` in `MonitoringController.Publish` and the share-connection failure status with this mapping.

- [ ] **Step 4: Translate incident transition labels**

Map `MonitorTransition.Alert` to `异常告警` and `MonitorTransition.Recovery` to `恢复正常`; keep the incident message machine-specific and Chinese.

- [ ] **Step 5: Run status and monitoring tests**

Run:

```powershell
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~MonitorStatusTextTests|FullyQualifiedName~MultiVmMonitorTests|FullyQualifiedName~SingleVmMonitorTests"
```

Expected: PASS with zero failures.

- [ ] **Step 6: Commit**

```powershell
git add src/WowVmMonitor.App/Presentation src/WowVmMonitor.Infrastructure/DesktopServices tests/WowVmMonitor.Tests/App
git commit -m "feat: localize monitor status text"
```

### Task 3: Chinese WPF Views

**Files:**
- Modify: `src/WowVmMonitor.Desktop/MainWindow.xaml`
- Modify: `src/WowVmMonitor.Desktop/Views/StatusView.xaml`
- Modify: `src/WowVmMonitor.Desktop/Views/SettingsView.xaml`
- Modify: `src/WowVmMonitor.Desktop/Views/IncidentHistoryView.xaml`
- Modify: `src/WowVmMonitor.App/History/IncidentHistoryViewModel.cs`
- Modify: `src/WowVmMonitor.App/Settings/SettingsViewModel.cs`
- Modify: `tests/WowVmMonitor.Desktop.Tests/WpfSmokeTests.cs`

- [ ] **Step 1: Extend the WPF smoke test with Chinese labels**

Create `MainWindow` on an STA thread and recursively inspect `TabItem`, `Button`, and `DataGridColumn` headers. Assert the window contains `监控状态`, `设置`, `异常历史`, `开始监控`, `停止监控`, `立即检测`, `机器`, `状态`, and `日志时间`.

- [ ] **Step 2: Run the smoke test and verify RED**

Run:

```powershell
dotnet test .\tests\WowVmMonitor.Desktop.Tests\WowVmMonitor.Desktop.Tests.csproj -c Release --filter "FullyQualifiedName~WpfSmokeTests"
```

Expected: FAIL because the current controls use English labels.

- [ ] **Step 3: Translate the main and status views**

Use these labels: `监控状态`, `设置`, `异常历史`, `开始监控`, `停止监控`, `立即检测`, `最近检测`, `机器`, `状态`, `共享路径`, `最新日志`, `日志时间`, `日志年龄`, `错误信息`.

- [ ] **Step 4: Translate settings and history views**

Use Chinese field names and tooltips for interval, timeout, warning, alert, username, password, add, remove, save, filters, refresh, and history columns. Change the in-memory history notice to `异常历史仅保存在内存中，程序退出后将被清空。` and save feedback to `设置已保存。` / `设置保存失败。`.

- [ ] **Step 5: Run the WPF smoke and settings tests**

Run:

```powershell
dotnet test .\tests\WowVmMonitor.Desktop.Tests\WowVmMonitor.Desktop.Tests.csproj -c Release --filter "FullyQualifiedName~WpfSmokeTests"
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~InMemoryIncidentHistoryTests"
```

Expected: both commands PASS with zero failures.

- [ ] **Step 6: Commit**

```powershell
git add src/WowVmMonitor.Desktop src/WowVmMonitor.App/History src/WowVmMonitor.App/Settings tests/WowVmMonitor.Desktop.Tests
git commit -m "feat: localize WPF views in Chinese"
```

### Task 4: Chinese Tray, Startup Messages, And Verification

**Files:**
- Modify: `src/WowVmMonitor.Desktop/Lifetime/NotifyIconHost.cs`
- Modify: `src/WowVmMonitor.Desktop/App.xaml.cs`
- Modify: `README.md`
- Test: `tests/WowVmMonitor.Desktop.Tests/WpfSmokeTests.cs`

- [ ] **Step 1: Add presentation text assertions**

Extract tray labels as internal constants visible to the desktop test project, then assert `打开主窗口`, `开始监控`, `停止监控`, and `退出程序`. Assert the startup error title is Chinese through an internal presentation-text holder rather than displaying a real message box.

- [ ] **Step 2: Run the presentation test and verify RED**

Run:

```powershell
dotnet test .\tests\WowVmMonitor.Desktop.Tests\WowVmMonitor.Desktop.Tests.csproj -c Release --filter "FullyQualifiedName~WpfSmokeTests"
```

Expected: FAIL because tray and startup strings are still English.

- [ ] **Step 3: Translate tray and startup text**

Replace the tray menu text with the four approved Chinese labels. Use `WowVmMonitor 启动失败` and `程序无法启动：{message}` for the startup error dialog. Keep raw exception details out of logs and preserve the existing bounded exit behavior.

- [ ] **Step 4: Update README**

Document that UI timestamps use the Windows local time zone while monitoring calculations remain UTC, and note that the desktop UI is Simplified Chinese.

- [ ] **Step 5: Run final verification**

Run:

```powershell
dotnet build .\WowVmMonitor.sln -c Release --no-restore
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~LocalTimeTextTests|FullyQualifiedName~MonitorStatusTextTests|FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~MonitoringDashboardViewModelTests|FullyQualifiedName~InMemoryIncidentHistoryTests"
dotnet test .\tests\WowVmMonitor.Desktop.Tests\WowVmMonitor.Desktop.Tests.csproj -c Release --no-build
git diff --check
```

Expected: build has zero warnings and errors; both test commands report zero failures; diff check produces no output. If Windows Application Control blocks the full infrastructure suite, report that separately and do not claim a full-suite pass.

- [ ] **Step 6: Commit**

```powershell
git add src/WowVmMonitor.Desktop README.md tests/WowVmMonitor.Desktop.Tests
git commit -m "feat: finish Chinese desktop localization"
```
