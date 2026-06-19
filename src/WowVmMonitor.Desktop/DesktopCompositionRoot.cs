using WowVmMonitor.App;
using WowVmMonitor.App.History;
using WowVmMonitor.App.Monitoring;
using WowVmMonitor.App.Settings;
using WowVmMonitor.Desktop.Lifetime;
using WowVmMonitor.Desktop.Ui;
using WowVmMonitor.Infrastructure.Configuration;
using WowVmMonitor.Infrastructure.Credentials;
using WowVmMonitor.Infrastructure.DesktopServices;
using WowVmMonitor.Infrastructure.Notifications;
using WowVmMonitor.Infrastructure.Shares;

namespace WowVmMonitor.Desktop;

internal sealed class DesktopCompositionRoot(System.Windows.Application application)
{
    private MonitoringDashboardViewModel? _dashboard;
    private NtfyNotificationDispatcher? _notificationDispatcher;
    private System.Net.Http.HttpClient? _httpClient;

    public async Task<MainWindow> CreateAsync(CancellationToken cancellationToken)
    {
        var dataDirectory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WowVmMonitor");
        var configurationStore = new ConfigurationStore(dataDirectory);
        var credentialStore = new WindowsCredentialStore(new WindowsCredentialNativeApi());
        var settingsService = new DesktopSettingsService(configurationStore, credentialStore);
        _httpClient = new System.Net.Http.HttpClient();
        var ntfyClient = new NtfyClient(_httpClient, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));
        _notificationDispatcher = new NtfyNotificationDispatcher(ntfyClient);
        var settings = await SettingsViewModel.CreateAsync(settingsService, cancellationToken, ntfyClient);
        var dispatcher = new WpfUiDispatcher(application.Dispatcher);
        var history = new InMemoryIncidentHistory(1_000);
        var shareConnections = new ShareConnectionCoordinator(credentialStore, new WindowsNetworkApi());
        var monitoring = new MonitoringController(configurationStore, shareConnections, history, _notificationDispatcher);
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
            monitoring,
            _notificationDispatcher);
        window.LifetimeController = lifetime;
        return window;
    }

    public async Task DisposeAsync()
    {
        if (_dashboard is not null)
        {
            await _dashboard.DisposeAsync();
        }
        if (_notificationDispatcher is not null)
        {
            await _notificationDispatcher.DisposeAsync();
        }
        _httpClient?.Dispose();
    }

    private static void Execute(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
