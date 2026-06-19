using WowVmMonitor.Core.Configuration;

namespace WowVmMonitor.App.Settings;

public interface ISettingsService
{
    Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken);

    Task<SettingsSaveResult> SaveAsync(
        SettingsSaveRequest request,
        CancellationToken cancellationToken);
}

public sealed record SettingsLoadResult(
    MonitorConfiguration Configuration,
    bool RequiresUserConfirmation,
    IReadOnlyList<ConfigurationMessage> Messages);

public sealed record SettingsSaveResult(
    bool Succeeded,
    IReadOnlyList<ConfigurationMessage> Messages);
