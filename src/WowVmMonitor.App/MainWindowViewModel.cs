using WowVmMonitor.App.History;
using WowVmMonitor.App.Monitoring;
using WowVmMonitor.App.Settings;

namespace WowVmMonitor.App;

public sealed record MainWindowViewModel(
    MonitoringDashboardViewModel Dashboard,
    SettingsViewModel Settings,
    IncidentHistoryViewModel History);
