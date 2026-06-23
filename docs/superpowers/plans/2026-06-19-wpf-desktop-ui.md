# WPF Desktop UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a responsive Windows 10 WPF application that edits settings, displays eight-machine status, starts and stops monitoring, queries in-memory incident history, and uses correct minimize, close, tray-open, and tray-exit behavior.

**Architecture:** Convert `WowVmMonitor.App` into a platform-neutral application library containing MVVM primitives, ViewModels, commands, and service interfaces. Add `WowVmMonitor.Desktop` as the only WPF executable and keep Windows Views, Dispatcher, `NotifyIcon`, and process lifecycle there; all file, credential, UNC, and monitoring work remains behind asynchronous application services.

**Tech Stack:** C# 12, .NET 8, WPF, Windows Forms `NotifyIcon`, MVVM, xUnit, STA WPF smoke tests.

---

## File Map

- Modify `src/WowVmMonitor.App/WowVmMonitor.App.csproj`: convert the existing console executable into a Core-only class library.
- Delete `src/WowVmMonitor.App/Program.cs`: Desktop becomes the executable composition root.
- Move `IShareConnectionCoordinator` and `ShareConnectionResult` from Infrastructure to `src/WowVmMonitor.App/Shares`: remove the existing App-to-Infrastructure dependency without changing behavior.
- Modify `src/WowVmMonitor.Infrastructure/WowVmMonitor.Infrastructure.csproj`: reference App to implement its service contracts.
- Create `src/WowVmMonitor.App/Mvvm/ObservableObject.cs`: property notification base.
- Create `src/WowVmMonitor.App/Mvvm/AsyncCommand.cs`: non-blocking, cancellable command with duplicate-execution prevention.
- Create `src/WowVmMonitor.App/Ui/IUiDispatcher.cs`: background-to-UI dispatch boundary.
- Create `src/WowVmMonitor.App/Monitoring/IMonitoringController.cs`: UI-facing monitoring operations and events.
- Create `src/WowVmMonitor.App/Monitoring/MachineStatusSnapshot.cs`: immutable secret-free UI status.
- Create `src/WowVmMonitor.App/Monitoring/MonitoringDashboardViewModel.cs`: status projection and commands.
- Create `src/WowVmMonitor.App/Monitoring/StatusUpdatePump.cs`: coalesce machine updates and dispatch at most four batches per second.
- Create `src/WowVmMonitor.App/History/*`: incident records, query interface, in-memory implementation, and ViewModel.
- Create `src/WowVmMonitor.App/Settings/*`: settings drafts, credential update ownership, service interface, and ViewModel.
- Create `src/WowVmMonitor.Infrastructure/DesktopServices/*`: adapters from configuration, credentials, and existing monitor types to App interfaces.
- Create `src/WowVmMonitor.Desktop/*`: WPF project, App resources, main window, pages, dispatcher adapter, tray adapter, and lifetime controller.
- Create `tests/WowVmMonitor.Desktop.Tests/*`: Windows-targeted lifecycle and WPF smoke tests.
- Add App-level ViewModel and responsiveness tests to `tests/WowVmMonitor.Tests`.
- Modify `WowVmMonitor.sln`, `build-and-test.ps1`, and `README.md`.

### Task 1: Create Platform-Neutral MVVM Primitives

**Files:**
- Modify: `src/WowVmMonitor.App/WowVmMonitor.App.csproj`
- Modify: `src/WowVmMonitor.Infrastructure/WowVmMonitor.Infrastructure.csproj`
- Delete: `src/WowVmMonitor.App/Program.cs`
- Create: `src/WowVmMonitor.App/Shares/IShareConnectionCoordinator.cs`
- Create: `src/WowVmMonitor.App/Shares/ShareConnectionResult.cs`
- Delete: `src/WowVmMonitor.Infrastructure/Shares/IShareConnectionCoordinator.cs`
- Delete: `src/WowVmMonitor.Infrastructure/Shares/ShareConnectionResult.cs`
- Modify: `src/WowVmMonitor.Infrastructure/Shares/ShareConnectionCoordinator.cs`
- Modify: `src/WowVmMonitor.App/StartupRunner.cs`
- Create: `src/WowVmMonitor.App/Mvvm/ObservableObject.cs`
- Create: `src/WowVmMonitor.App/Mvvm/AsyncCommand.cs`
- Create: `src/WowVmMonitor.App/Ui/IUiDispatcher.cs`
- Create: `tests/WowVmMonitor.Tests/App/AsyncCommandTests.cs`

- [ ] **Step 1: Write failing async-command tests**

```csharp
[Fact]
public async Task ExecuteReturnsBeforeSlowOperationCompletesAndRejectsDuplicateExecution()
{
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var calls = 0;
    var command = new AsyncCommand(async cancellationToken =>
    {
        calls++;
        await gate.Task.WaitAsync(cancellationToken);
    });

    command.Execute(null);
    command.Execute(null);

    Assert.True(command.IsExecuting);
    Assert.Equal(1, calls);
    gate.SetResult();
    await command.ExecutionTask;
    Assert.False(command.IsExecuting);
}

[Fact]
public async Task CancelStopsOperationAndResetsBusyState()
{
    var command = new AsyncCommand(token => Task.Delay(Timeout.InfiniteTimeSpan, token));

    command.Execute(null);
    command.Cancel();
    await command.ExecutionTask;

    Assert.False(command.IsExecuting);
    Assert.True(command.CanExecute(null));
}
```

- [ ] **Step 2: Run and verify RED**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj --filter "FullyQualifiedName~AsyncCommandTests"
```

Expected: compilation fails because `AsyncCommand` does not exist.

- [ ] **Step 3: Convert App to a Core-only library and move service contracts**

Remove `<OutputType>Exe</OutputType>` and the Infrastructure project reference from `WowVmMonitor.App.csproj`, then delete `Program.cs`. Move `IShareConnectionCoordinator` and `ShareConnectionResult` into `WowVmMonitor.App.Shares`; update `StartupRunner` to use that namespace. Add an App project reference to Infrastructure, and update `ShareConnectionCoordinator` to implement the moved interface. This establishes `Infrastructure -> App -> Core` and prevents a circular reference when Infrastructure implements UI services.

- [ ] **Step 4: Implement primitives**

`ObservableObject` implements `INotifyPropertyChanged` and a protected `SetProperty<T>` using `EqualityComparer<T>.Default`.

`AsyncCommand` implements `ICommand`, exposes `IsExecuting`, `ExecutionTask`, `Cancel`, and `CanExecute`. `Execute` assigns `ExecutionTask = ExecuteCoreAsync()` and returns immediately. `ExecuteCoreAsync` creates one cancellation source, raises `CanExecuteChanged`, awaits the delegate, treats command-owned cancellation as expected, then clears busy state and disposes the source in `finally`. It never uses `async void` except the `ICommand.Execute` boundary, and all exceptions are captured in a public `Exception? LastError` rather than becoming unobserved.

```csharp
public interface IUiDispatcher
{
    bool CheckAccess();
    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Run tests and commit**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj --filter "FullyQualifiedName~AsyncCommandTests"
& 'C:\Program Files\Git\cmd\git.exe' add src/WowVmMonitor.App src/WowVmMonitor.Infrastructure tests/WowVmMonitor.Tests/App/AsyncCommandTests.cs
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: add platform-neutral mvvm commands"
```

### Task 2: Build Monitoring Status Projection And In-Memory History

**Files:**
- Create: `src/WowVmMonitor.App/Monitoring/IMonitoringController.cs`
- Create: `src/WowVmMonitor.App/Monitoring/MachineStatusSnapshot.cs`
- Create: `src/WowVmMonitor.App/Monitoring/MachineStatusViewModel.cs`
- Create: `src/WowVmMonitor.App/Monitoring/MonitoringDashboardViewModel.cs`
- Create: `src/WowVmMonitor.App/Monitoring/StatusUpdatePump.cs`
- Create: `src/WowVmMonitor.App/History/IncidentRecord.cs`
- Create: `src/WowVmMonitor.App/History/IncidentQuery.cs`
- Create: `src/WowVmMonitor.App/History/IIncidentHistoryReader.cs`
- Create: `src/WowVmMonitor.App/History/InMemoryIncidentHistory.cs`
- Create: `src/WowVmMonitor.App/History/IncidentHistoryViewModel.cs`
- Create: `tests/WowVmMonitor.Tests/App/MonitoringDashboardViewModelTests.cs`
- Create: `tests/WowVmMonitor.Tests/App/InMemoryIncidentHistoryTests.cs`

- [ ] **Step 1: Write failing dashboard responsiveness test**

```csharp
[Fact]
public async Task SlowStartDoesNotBlockAndStatusUpdatesUseDispatcher()
{
    var controller = new BlockingMonitoringController();
    var dispatcher = new RecordingDispatcher();
    await using var viewModel = new MonitoringDashboardViewModel(controller, dispatcher);

    viewModel.StartCommand.Execute(null);
    controller.Publish(new MachineStatusSnapshot(
        "vm-01", "VM 01", "Normal", @"\\server\share", "activity.log",
        DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5), null));

    Assert.True(viewModel.StartCommand.IsExecuting);
    controller.ReleaseStart();
    await viewModel.StartCommand.ExecutionTask;
    await viewModel.FlushStatusUpdatesAsync();

    Assert.True(dispatcher.InvokeCount > 0);
    Assert.Equal("Normal", viewModel.Machines.Single().StatusText);
}
```

- [ ] **Step 2: Write failing history query test**

```csharp
[Fact]
public async Task QueryFiltersByMachineAndEventType()
{
    var history = new InMemoryIncidentHistory(capacity: 100);
    history.Append(new IncidentRecord(DateTimeOffset.UtcNow, "vm-01", "Alert", "alert"));
    history.Append(new IncidentRecord(DateTimeOffset.UtcNow, "vm-02", "Recovery", "recovery"));

    var records = await history.QueryAsync(
        new IncidentQuery("vm-01", "Alert"),
        CancellationToken.None);

    var record = Assert.Single(records);
    Assert.Equal("vm-01", record.MachineId);
}
```

- [ ] **Step 3: Run and verify RED**

Run filters for `MonitoringDashboardViewModelTests` and `InMemoryIncidentHistoryTests`. Expected: compilation fails because monitoring and history application types do not exist.

- [ ] **Step 4: Implement controller contract and dashboard**

```csharp
public interface IMonitoringController
{
    bool IsRunning { get; }
    event EventHandler<MachineStatusSnapshot>? MachineStatusChanged;
    event EventHandler<DateTimeOffset>? CycleCompleted;
    Task CheckOnceAsync(CancellationToken cancellationToken);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

`MonitoringDashboardViewModel` owns an `ObservableCollection<MachineStatusViewModel>`, Start, Stop, and Check Once commands, overall state, last-cycle time, and `StatusUpdatePump`. Start is enabled only when not running; Stop only when running; a confirmation-required property disables Start and Check Once.

`StatusUpdatePump` stores the latest snapshot per machine in a `ConcurrentDictionary`, uses one background loop with a 250 ms interval, and dispatches a single batch through `IUiDispatcher`. Alert and Recovery snapshots replace older snapshots for the same machine but are flushed immediately. Disposal cancels and awaits the pump.

- [ ] **Step 5: Implement memory history**

`InMemoryIncidentHistory` locks a bounded queue, retains only the newest configured capacity, exposes synchronous `Append`, and returns copied immutable arrays from `QueryAsync`. `IncidentHistoryViewModel` owns filter properties, a refresh command, `ObservableCollection<IncidentRecord>`, and the text `History is kept in memory until WowVmMonitor exits.`

- [ ] **Step 6: Run tests and commit**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj --filter "FullyQualifiedName~MonitoringDashboardViewModelTests|FullyQualifiedName~InMemoryIncidentHistoryTests"
& 'C:\Program Files\Git\cmd\git.exe' add src/WowVmMonitor.App/Monitoring src/WowVmMonitor.App/History tests/WowVmMonitor.Tests/App
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: project monitoring state for ui"
```

### Task 3: Add Asynchronous Settings Editing With Secret Clearing

**Files:**
- Create: `src/WowVmMonitor.App/Settings/ISettingsService.cs`
- Create: `src/WowVmMonitor.App/Settings/SettingsSaveRequest.cs`
- Create: `src/WowVmMonitor.App/Settings/MachineSettingsDraft.cs`
- Create: `src/WowVmMonitor.App/Settings/SettingsViewModel.cs`
- Create: `src/WowVmMonitor.Infrastructure/DesktopServices/DesktopSettingsService.cs`
- Create: `tests/WowVmMonitor.Tests/App/SettingsViewModelTests.cs`

- [ ] **Step 1: Write failing secret-lifetime tests**

```csharp
[Theory]
[InlineData(false)]
[InlineData(true)]
public async Task PasswordBufferIsClearedAfterSave(bool failSave)
{
    var service = new RecordingSettingsService(failSave);
    var viewModel = await SettingsViewModel.CreateAsync(service, CancellationToken.None);
    var draft = viewModel.Machines.Single();
    draft.SetReplacementPassword("unique-ui-password".ToCharArray());
    var observed = draft.ReplacementPassword;

    viewModel.SaveCommand.Execute(null);
    await viewModel.SaveCommand.ExecutionTask;

    Assert.True(observed.Span.ToArray().All(character => character == '\0'));
    Assert.DoesNotContain("unique-ui-password", viewModel.ToString(), StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run and verify RED**

Run the `SettingsViewModelTests` filter. Expected: compilation fails because settings types do not exist.

- [ ] **Step 3: Implement settings contracts and ViewModel**

`ISettingsService.LoadAsync` returns the secret-free `MonitorConfiguration`. `SaveAsync` accepts `SettingsSaveRequest`, validates through existing `ConfigurationValidator`, writes JSON through `ConfigurationStore`, and applies only non-empty credential updates through `WindowsCredentialStore` on a worker task.

`MachineSettingsDraft` owns a private replacement-password array, exposes a read-only memory view only to save assembly code, returns `[REDACTED]` from `ToString`, and clears the array in `ClearReplacementPassword`. `SettingsViewModel` always clears every password draft in a `finally` block around save. Blank password creates no credential update.

- [ ] **Step 4: Run settings and existing secret tests, then commit**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj --filter "FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~SecretLeakageTests"
& 'C:\Program Files\Git\cmd\git.exe' add src/WowVmMonitor.App/Settings src/WowVmMonitor.Infrastructure/DesktopServices tests/WowVmMonitor.Tests/App/SettingsViewModelTests.cs
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: edit settings through async services"
```

### Task 4: Scaffold The WPF Desktop And Main Pages

**Files:**
- Create: `src/WowVmMonitor.Desktop/WowVmMonitor.Desktop.csproj`
- Create: `src/WowVmMonitor.Desktop/App.xaml`
- Create: `src/WowVmMonitor.Desktop/App.xaml.cs`
- Create: `src/WowVmMonitor.Desktop/MainWindow.xaml`
- Create: `src/WowVmMonitor.Desktop/MainWindow.xaml.cs`
- Create: `src/WowVmMonitor.Desktop/Views/StatusView.xaml`
- Create: `src/WowVmMonitor.Desktop/Views/SettingsView.xaml`
- Create: `src/WowVmMonitor.Desktop/Views/IncidentHistoryView.xaml`
- Create: `src/WowVmMonitor.Desktop/Ui/WpfUiDispatcher.cs`
- Modify: `WowVmMonitor.sln`
- Create: `tests/WowVmMonitor.Desktop.Tests/WowVmMonitor.Desktop.Tests.csproj`
- Create: `tests/WowVmMonitor.Desktop.Tests/WpfSmokeTests.cs`

- [ ] **Step 1: Add a failing WPF smoke test project**

The Desktop test project targets `net8.0-windows`, sets `EnableWindowsTargeting`, references Desktop, and uses the same xUnit package versions. Add this STA smoke test:

```csharp
[Fact]
public void MainWindowCanBeCreatedOnStaThread()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            _ = new MainWindow();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    Assert.Null(failure);
}
```

- [ ] **Step 2: Run and verify RED**

Run the Desktop test project. Expected: build fails because Desktop and `MainWindow` do not exist.

- [ ] **Step 3: Create the WPF project**

Use this project shape:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\WowVmMonitor.App\WowVmMonitor.App.csproj" />
    <ProjectReference Include="..\WowVmMonitor.Core\WowVmMonitor.Core.csproj" />
    <ProjectReference Include="..\WowVmMonitor.Infrastructure\WowVmMonitor.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Implement the three pages**

`MainWindow` uses a `TabControl` with Status, Settings, and Incident History tabs. `StatusView` uses a virtualized read-only `DataGrid` bound to machine status columns and top-level Start/Stop/Check Once buttons. `SettingsView` uses an eight-row `ItemsControl`; PasswordBox code-behind reads `SecurePassword`, copies it through a temporary BSTR into a new `char[]`, zeroes and frees the BSTR in `finally`, and transfers the array into the draft without calling `PasswordBox.Password`. Save and Cancel controls never log credential input. `IncidentHistoryView` uses filter ComboBoxes, Refresh, a read-only DataGrid, empty-state text, and the memory-only notice.

All bindings use `UpdateSourceTrigger=PropertyChanged` only for local text editing. No code-behind calls Core or Infrastructure; it only forwards UI gestures and password characters to ViewModel APIs.

- [ ] **Step 5: Implement Dispatcher and run smoke test**

`WpfUiDispatcher` wraps `System.Windows.Threading.Dispatcher`, executes inline when `CheckAccess` is true, otherwise awaits `Dispatcher.InvokeAsync` with cancellation. Run the WPF smoke test and commit the scaffold.

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\WowVmMonitor.Desktop.Tests\WowVmMonitor.Desktop.Tests.csproj
& 'C:\Program Files\Git\cmd\git.exe' add WowVmMonitor.sln src/WowVmMonitor.Desktop tests/WowVmMonitor.Desktop.Tests
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: add wpf monitoring desktop"
```

### Task 5: Implement Correct Tray And Window Lifetime

**Files:**
- Create: `src/WowVmMonitor.Desktop/Lifetime/IMainWindowHost.cs`
- Create: `src/WowVmMonitor.Desktop/Lifetime/ITrayIconHost.cs`
- Create: `src/WowVmMonitor.Desktop/Lifetime/IApplicationShutdown.cs`
- Create: `src/WowVmMonitor.Desktop/Lifetime/DesktopLifetimeController.cs`
- Create: `src/WowVmMonitor.Desktop/Lifetime/WpfMainWindowHost.cs`
- Create: `src/WowVmMonitor.Desktop/Lifetime/NotifyIconHost.cs`
- Create: `src/WowVmMonitor.Desktop/Lifetime/WpfApplicationShutdown.cs`
- Modify: `src/WowVmMonitor.Desktop/MainWindow.xaml.cs`
- Modify: `src/WowVmMonitor.Desktop/App.xaml.cs`
- Create: `tests/WowVmMonitor.Desktop.Tests/DesktopLifetimeControllerTests.cs`

- [ ] **Step 1: Write failing lifecycle tests**

```csharp
[Fact]
public void MinimizeAndCloseHideWithoutStopping()
{
    var fixture = new LifetimeFixture();

    fixture.Controller.OnMinimized();
    var cancelClose = fixture.Controller.OnWindowClosing();

    Assert.True(cancelClose);
    Assert.Equal(2, fixture.Window.HideCount);
    Assert.Equal(0, fixture.Monitoring.StopCount);
    Assert.False(fixture.Shutdown.WasCalled);
}

[Fact]
public async Task TrayExitStopsDisposesAndShutsDownInOrder()
{
    var fixture = new LifetimeFixture();

    await fixture.Controller.ExitAsync(CancellationToken.None);

    Assert.Equal(["stop", "tray.dispose", "shutdown"], fixture.Events);
    Assert.False(fixture.Controller.OnWindowClosing());
}
```

- [ ] **Step 2: Run and verify RED**

Run `DesktopLifetimeControllerTests`. Expected: compilation fails because lifetime types do not exist.

- [ ] **Step 3: Implement lifetime controller**

`DesktopLifetimeController` owns an atomic exit-requested flag. Minimize calls Hide. Closing returns `true` and calls Hide unless exit was requested. Open calls Show, Restore, and Activate on the existing window. Exit uses `Interlocked.Exchange` to run once, disables the tray, links a five-second timeout, awaits monitoring Stop, disposes the tray in `finally`, then invokes application shutdown.

`NotifyIconHost` creates exactly one `NotifyIcon`, wires Open/Start/Stop/Exit menu items, routes callbacks to async application commands without blocking the WinForms event, and disposes menu and icon. `App.xaml` sets `ShutdownMode=OnExplicitShutdown`.

- [ ] **Step 4: Wire Windows shutdown**

Handle WPF session-ending by setting exit requested and starting bounded cleanup without displaying dialogs. Do not cancel the Windows session end. Window `StateChanged` routes minimized state to lifetime controller; `Closing` sets `Cancel` from `OnWindowClosing`.

- [ ] **Step 5: Run lifecycle tests and commit**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\WowVmMonitor.Desktop.Tests\WowVmMonitor.Desktop.Tests.csproj --filter "FullyQualifiedName~DesktopLifetimeControllerTests"
& 'C:\Program Files\Git\cmd\git.exe' add src/WowVmMonitor.Desktop tests/WowVmMonitor.Desktop.Tests/DesktopLifetimeControllerTests.cs
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: add tray-first desktop lifetime"
```

### Task 6: Compose Services And Verify Responsiveness

**Files:**
- Create: `src/WowVmMonitor.Infrastructure/DesktopServices/MonitoringController.cs`
- Create: `src/WowVmMonitor.Desktop/DesktopCompositionRoot.cs`
- Modify: `src/WowVmMonitor.Desktop/App.xaml.cs`
- Create: `tests/WowVmMonitor.Tests/App/UiResponsivenessTests.cs`
- Modify: `build-and-test.ps1`
- Modify: `README.md`

- [ ] **Step 1: Write failing responsiveness test**

```csharp
[Fact]
public async Task BlockingMachineDoesNotPreventCommandsOrOtherStatusRows()
{
    var controller = new PartiallyBlockingMonitoringController("vm-01");
    var dispatcher = new RecordingDispatcher();
    await using var viewModel = new MonitoringDashboardViewModel(controller, dispatcher);

    viewModel.CheckOnceCommand.Execute(null);
    controller.PublishNormal("vm-02");
    await viewModel.FlushStatusUpdatesAsync();

    Assert.True(viewModel.CheckOnceCommand.IsExecuting);
    Assert.Equal("Normal", viewModel.Machines.Single(row => row.Id == "vm-02").StatusText);
    Assert.True(viewModel.StopCommand.CanExecute(null));
    controller.Release();
    await viewModel.CheckOnceCommand.ExecutionTask;
}
```

- [ ] **Step 2: Run and verify RED**

Expected: test fails until controller composition and command enablement permit independent UI activity.

- [ ] **Step 3: Implement monitoring adapter and composition root**

`MonitoringController` wraps the existing `MultiVmMonitor`, converts `MultiVmMonitorResult` into `MachineStatusSnapshot`, and raises events without invoking UI code. `DesktopCompositionRoot` loads configuration, creates distinct `SharedLogActivitySource`, `LogMonitorStateMachine`, and `SingleVmMonitor` instances per enabled machine, creates settings/history/controller/ViewModels, then constructs MainWindow and tray lifetime objects.

All UNC and Credential Manager setup runs inside service tasks before monitor start. Composition surfaces configuration recovery messages and passes `RequiresUserConfirmation` to the dashboard.

- [ ] **Step 4: Update build and documentation**

Ensure `build-and-test.ps1` discovers both test projects and fails if either executes zero tests. README documents the three UI pages, minimize/close-to-tray behavior, tray-only Exit, memory-only history, and that UI commands never perform direct file or network work.

- [ ] **Step 5: Run complete Release verification**

```powershell
powershell -ExecutionPolicy Bypass -File .\build-and-test.ps1
```

Expected: Desktop and all existing projects build in Release with zero warnings and errors; App tests, Desktop lifecycle tests, WPF smoke tests, and the existing suite all pass.

- [ ] **Step 6: Perform static UI-thread guard scan**

```powershell
rg -n "\.Result\b|\.Wait\(|File\.|Directory\.|Cred(Read|Write|Delete)|WNetAddConnection" src/WowVmMonitor.Desktop src/WowVmMonitor.App
```

Expected: no `.Result`, `.Wait()`, direct file, Credential Manager, or UNC calls in Desktop or ViewModel code. Allowed matches are only service-interface names or documentation comments and must be reviewed individually.

- [ ] **Step 7: Commit composition and docs**

```powershell
& 'C:\Program Files\Git\cmd\git.exe' add src/WowVmMonitor.Infrastructure/DesktopServices src/WowVmMonitor.Desktop tests build-and-test.ps1 README.md
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: compose responsive desktop monitoring"
```

## Final Verification Checklist

- [ ] Desktop is the WPF executable; App contains no WPF references.
- [ ] Status page shows eight independent machine rows and last complete-check time.
- [ ] Settings save and credential updates are asynchronous and password buffers are cleared.
- [ ] Incident history refreshes and filters through `IIncidentHistoryReader` and is marked memory-only.
- [ ] Start, Stop, Check Once, Save, and Refresh prevent duplicate execution.
- [ ] All background status updates pass through `IUiDispatcher`.
- [ ] High-frequency status updates are coalesced without losing alert or recovery state.
- [ ] Minimize and Close hide to tray without stopping monitoring.
- [ ] tray Open restores the existing window.
- [ ] tray Exit stops, disposes, and shuts down in the tested order.
- [ ] Windows shutdown uses bounded cleanup and no confirmation dialog.
- [ ] Desktop and ViewModel code contain no direct file, Credential Manager, UNC, `.Result`, or `.Wait()` calls.
- [ ] Release build reports zero warnings and zero errors.
- [ ] Every automated test project executes tests with zero failures.
