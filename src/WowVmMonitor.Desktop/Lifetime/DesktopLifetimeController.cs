using WowVmMonitor.App.Monitoring;

namespace WowVmMonitor.Desktop.Lifetime;

public sealed class DesktopLifetimeController
{
    private readonly IMainWindowHost _window;
    private readonly ITrayIconHost _tray;
    private readonly IApplicationShutdown _shutdown;
    private readonly IMonitoringController _monitoring;
    private readonly IAsyncDisposable? _backgroundCleanup;
    private int _exitRequested;

    public DesktopLifetimeController(
        IMainWindowHost window,
        ITrayIconHost tray,
        IApplicationShutdown shutdown,
        IMonitoringController monitoring,
        IAsyncDisposable? backgroundCleanup = null)
    {
        _window = window;
        _tray = tray;
        _shutdown = shutdown;
        _monitoring = monitoring;
        _backgroundCleanup = backgroundCleanup;
    }

    public void OnMinimized() => _window.Hide();

    public bool OnWindowClosing()
    {
        if (Volatile.Read(ref _exitRequested) != 0)
        {
            return false;
        }

        _window.Hide();
        return true;
    }

    public void Open()
    {
        _window.Show();
        _window.Restore();
        _window.Activate();
    }

    public async Task ExitAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _exitRequested, 1) != 0)
        {
            return;
        }

        _tray.DisableCommands();
        try
        {
            await _monitoring
                .StopAsync(cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException)
        {
        }
        finally
        {
            if (_backgroundCleanup is not null)
            {
                try
                {
                    await _backgroundCleanup.DisposeAsync().AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (TimeoutException)
                {
                }
            }
            _tray.Dispose();
            _shutdown.Shutdown();
        }
    }
}
