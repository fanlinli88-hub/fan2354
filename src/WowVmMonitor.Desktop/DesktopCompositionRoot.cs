using WowVmMonitor.App;
using WowVmMonitor.App.History;
using WowVmMonitor.App.Monitoring;
using WowVmMonitor.App.Settings;
using WowVmMonitor.Desktop.Lifetime;
using WowVmMonitor.Desktop.Ui;
using WowVmMonitor.Infrastructure.Configuration;
using WowVmMonitor.Infrastructure.Credentials;
using WowVmMonitor.Infrastructure.DesktopServices;
using WowVmMonitor.Infrastructure.Shares;

namespace WowVmMonitor.Desktop;

internal sealed class DesktopCompositionRoot(System.Windows.Application application)
{
    private MonitoringDashboardViewModel? _dashboard;

    public async Task<MainWindow> CreateAsync(CancellationToken cancellationToken)
    {
        var dataDirectory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WowVmMonitor");
        var configurationStore = new ConfigurationStore(dataDirectory);
        var credentialStore = new WindowsCredentialStore(new WindowsCredentialNativeApi());
        var settingsService = new DesktopSettingsService(configurationStore, credentialStore);
        var settings = await SettingsViewModel.CreateAsync(settingsService, cancellationToken);
        var dispatcher = new WpfUiDispatcher(application.Dispatcher);
        var history = new InMemoryIncidentHistory(1_000);
        var shareConnections = new ShareConnectionCoordinator(credentialStore, new WindowsNetworkApi());
        var monitoring = new MonitoringController(configurationStore, shareConnections, history);
        _dashboard = new MonitoringDashboardViewModel(monitoring, dispatcher, settings.RequiresUserConfirmation);
        settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.RequiresUserConfirmation))
            {
                _dashboard.RequiresUserConfirmation = settings.RequiresUserConfirmation;
            }
        };
        var historyViewModel = new IncidentHistoryViewModel(history, dispatcher);
        var window = new MainWindow
        {
            DataContext = new MainWindowViewModel(_dashboard, settings, historyViewModel)
        };

        DesktopLifetimeController? lifetime = null;
        var tray = new NotifyIconHost(
            () => lifetime?.Open(),
            () => Execute(_dashboard.StartCommand),
            () => Execute(_dashboard.StopCommand),
            () => lifetime?.ExitAsync(CancellationToken.None) ?? Task.CompletedTask);
        lifetime = new DesktopLifetimeController(
            new WpfMainWindowHost(window),
            tray,
            new WpfApplicationShutdown(application),
            monitoring);
        window.LifetimeController = lifetime;
        return window;
    }

    public async Task DisposeAsync()
    {
        if (_dashboard is not null)
        {
            await _dashboard.DisposeAsync();
        }
    }

    private static void Execute(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
