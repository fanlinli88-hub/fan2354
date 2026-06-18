using WowVmMonitor.App.Monitoring;
using WowVmMonitor.Desktop.Lifetime;

namespace WowVmMonitor.Desktop.Tests;

public sealed class DesktopLifetimeControllerTests
{
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

    [Fact]
    public void OpenRestoresAndActivatesExistingWindow()
    {
        var fixture = new LifetimeFixture();

        fixture.Controller.Open();

        Assert.Equal(1, fixture.Window.ShowCount);
        Assert.Equal(1, fixture.Window.RestoreCount);
        Assert.Equal(1, fixture.Window.ActivateCount);
    }

    private sealed class LifetimeFixture
    {
        public List<string> Events { get; } = [];
        public RecordingWindow Window { get; } = new();
        public RecordingMonitoring Monitoring { get; }
        public RecordingTray Tray { get; }
        public RecordingShutdown Shutdown { get; }
        public DesktopLifetimeController Controller { get; }

        public LifetimeFixture()
        {
            Monitoring = new RecordingMonitoring(Events);
            Tray = new RecordingTray(Events);
            Shutdown = new RecordingShutdown(Events);
            Controller = new DesktopLifetimeController(Window, Tray, Shutdown, Monitoring);
        }
    }

    private sealed class RecordingWindow : IMainWindowHost
    {
        public int HideCount { get; private set; }
        public int ShowCount { get; private set; }
        public int RestoreCount { get; private set; }
        public int ActivateCount { get; private set; }
        public void Hide() => HideCount++;
        public void Show() => ShowCount++;
        public void Restore() => RestoreCount++;
        public void Activate() => ActivateCount++;
    }

    private sealed class RecordingTray(List<string> events) : ITrayIconHost
    {
        public void DisableCommands() { }
        public void Dispose() => events.Add("tray.dispose");
    }

    private sealed class RecordingShutdown(List<string> events) : IApplicationShutdown
    {
        public bool WasCalled { get; private set; }
        public void Shutdown()
        {
            WasCalled = true;
            events.Add("shutdown");
        }
    }

    private sealed class RecordingMonitoring(List<string> events) : IMonitoringController
    {
        public int StopCount { get; private set; }
        public bool IsRunning => true;
        public event Action<MachineStatusSnapshot>? MachineStatusChanged { add { } remove { } }
        public event Action<DateTimeOffset>? CycleCompleted { add { } remove { } }
        public Task CheckOnceAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            events.Add("stop");
            return Task.CompletedTask;
        }
    }
}
