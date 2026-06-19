# WPF Desktop UI Design

## Goal

Provide a responsive Windows 10 desktop UI for editing settings, displaying eight-machine status, starting and stopping monitoring, viewing in-memory incident history, and running from the system tray. Closing or minimizing the window must keep monitoring active; only the tray Exit command terminates the process.

## Scope

This increment builds the WPF shell, platform-neutral ViewModels, asynchronous commands, UI dispatching, tray lifecycle, and an in-memory incident-history query implementation. It does not add persistent incident history, ntfy notification delivery, or automatic Windows startup registration.

## Project Structure

Add `WowVmMonitor.Desktop`, targeting Windows 10 with WPF and Windows Forms enabled. It contains only Views, WPF resources, Dispatcher integration, `NotifyIcon`, and window/process lifecycle code.

Keep `WowVmMonitor.App` free of WPF references. It contains ViewModels, async commands, UI-facing data models, monitoring and settings service interfaces, application controllers, and incident-history query abstractions. Existing Core and Infrastructure projects retain monitoring, configuration, credentials, file, and network responsibilities.

Dependencies flow in one direction:

```text
WowVmMonitor.Desktop -> WowVmMonitor.App
WowVmMonitor.Desktop -> Core + Infrastructure composition
WowVmMonitor.App -> Core + narrow Infrastructure service abstractions
Infrastructure -> Core
```

No Core or Infrastructure type references a WPF control, `Dispatcher`, `Window`, or `NotifyIcon`.

## Main Window

The main window contains three navigation pages.

### Status Page

The status page displays:

- overall monitoring state;
- last completed full-check time;
- Start and Stop commands;
- one row for each of up to eight configured machines;
- machine name and enabled state;
- current status;
- UNC share path;
- latest log path;
- latest log modification time;
- calculated log age;
- machine-scoped error code when present.

Status uses text plus color so it remains understandable without color perception. Disabled, unavailable, normal, warning, alert, recovery, and unexpected-error states have explicit labels.

### Settings Page

The settings page edits:

- machine display name;
- UNC path;
- enabled state;
- independent share username;
- replacement password;
- check interval;
- check timeout;
- warning and alert thresholds.

Passwords are blank on load. A blank password means the existing Credential Manager value is unchanged. A non-empty password is passed to the settings service through a short-lived character buffer, cleared after save success or failure, and never included in validation messages or `ToString()` output.

Save is asynchronous. The ViewModel validates local shape immediately, then delegates JSON and Credential Manager operations to `ISettingsService`. While saving, duplicate save is disabled and a cancellable busy indicator is shown.

### Incident History Page

The history page depends only on `IIncidentHistoryReader`. It supports asynchronous refresh, machine filtering, event-type filtering, empty-state text, and a visible notice that history is memory-only in this increment.

`InMemoryIncidentHistory` is thread-safe and receives important monitoring events. Restarting the process clears this history. A future persistent implementation can replace the reader without changing Views or ViewModels.

## Application Interfaces

`IMonitoringController` exposes:

- `CheckOnceAsync`;
- `StartAsync`;
- `StopAsync`;
- `IsRunning`;
- machine-status events;
- complete-cycle events.

`ISettingsService` exposes asynchronous load and save operations. It returns structured validation and operation results rather than throwing secret-bearing exceptions into the UI.

`IIncidentHistoryReader` exposes an asynchronous query with optional machine and event filters.

`IUiDispatcher` exposes `InvokeAsync` and represents the only allowed path from background status events to observable UI state.

The WPF composition root adapts the existing configuration, credential, share-connection, and multi-machine monitoring services to these interfaces.

## Commands And Responsiveness

All buttons use `IAsyncCommand`. Commands begin asynchronously and return control to the WPF message loop before any file, Credential Manager, UNC, or monitoring operation occurs.

The UI layer must not use:

- `.Result`;
- `.Wait()`;
- synchronous UNC enumeration;
- direct configuration file access;
- direct Credential Manager calls;
- direct monitoring-loop execution.

Start, Stop, Check Once, Save, and Refresh History prevent duplicate execution. Their cancellation tokens are linked to the application lifetime. Expected errors become structured ViewModel messages; unhandled background exceptions are observed and converted at the application-service boundary.

Machine status events enter a background-safe queue. A status projection component coalesces updates per machine and dispatches batches to the UI at most four times per second. A final alert or recovery update is never dropped. This avoids overwhelming WPF bindings when events arrive quickly.

## Window And Tray Lifecycle

Minimize and window Close both hide the main window and keep monitoring active. They do not call the monitoring Stop command and do not end the process.

The tray icon provides:

- Open;
- Start Monitoring;
- Stop Monitoring;
- Exit.

Double-clicking the icon performs Open. Open restores a minimized or hidden window, normalizes its state, activates it, and brings it to the foreground.

Tray Exit is the only normal process-termination path. Exit follows this order:

1. mark application exit as requested so window Closing is no longer converted to Hide;
2. disable tray commands;
3. cancel application-lifetime work;
4. await monitoring stop with a bounded timeout;
5. dispose the tray icon;
6. shut down the WPF application.

Windows shutdown or user logoff does not display confirmation dialogs or block indefinitely. It requests the same bounded cleanup and permits Windows to continue even if the timeout expires.

## UI State And Errors

ViewModels expose immutable snapshots or UI-specific records rather than mutable infrastructure objects. Machine identity is stable and status rows update in place so selection and scroll position remain stable.

Configuration corruption and backup recovery messages from `ConfigurationLoadResult` are displayed prominently. If `RequiresUserConfirmation` is true, Start and Check Once remain disabled until the user reviews and saves settings.

Machine-scoped share, credential, and monitoring errors do not disable controls for the other seven machines. UI messages contain machine ID, operation code, and safe Win32 code only. Passwords and usernames are never included.

## Testing Strategy

Platform-neutral App tests cover:

- Start, Stop, Check Once, Save, and History Refresh commands;
- duplicate command prevention;
- command cancellation and busy-state reset;
- confirmation-required configuration disables monitoring commands;
- slow fake services do not block the calling synchronization context;
- every background status update reaches observable state through `IUiDispatcher`;
- high-frequency events are coalesced while final alert and recovery updates remain visible;
- one slow or failed machine does not block other rows or commands;
- password buffers are cleared after settings save success and failure;
- ViewModel strings, messages, and serialized state contain no credential values.

Desktop lifecycle tests cover:

- Minimize hides the window and preserves monitoring;
- Close hides the window and preserves monitoring;
- tray Open restores and activates the window;
- tray Exit stops monitoring, disposes the icon, and terminates;
- repeated Open and Hide operations do not create multiple tray icons;
- Windows shutdown uses bounded cleanup without showing UI prompts.

A WPF smoke test creates the application resources and main window on an STA thread. Complete Release verification builds all projects with zero warnings and errors and runs the full test suite.

## Acceptance Criteria

- The UI edits settings through an asynchronous application service.
- The UI displays up to eight independent machine statuses and the last complete-check time.
- Start, Stop, and Check Once remain responsive during slow monitoring, file, credential, and network operations.
- Incident history can be refreshed and filtered through `IIncidentHistoryReader`; persistence is explicitly deferred.
- Minimize and Close hide to tray without stopping monitoring.
- tray Open restores the existing main window.
- tray Exit is the only normal exit path and releases monitoring tasks and the tray icon.
- Windows shutdown and logoff are not blocked indefinitely.
- UI code performs no direct file, Credential Manager, UNC, or monitoring work.
- Passwords and usernames do not enter configuration, logs, UI errors, ViewModel strings, or history.
- Release build completes with zero warnings and zero errors, and all automated tests pass.
