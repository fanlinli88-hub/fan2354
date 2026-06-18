namespace WowVmMonitor.Desktop;

public partial class App : System.Windows.Application
{
    private DesktopCompositionRoot? _compositionRoot;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            _compositionRoot = new DesktopCompositionRoot(this);
            var window = await _compositionRoot.CreateAsync(CancellationToken.None);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"WowVmMonitor could not start: {exception.Message}",
                "WowVmMonitor",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_compositionRoot is not null)
        {
            await _compositionRoot.DisposeAsync();
        }

        base.OnExit(e);
    }
}
