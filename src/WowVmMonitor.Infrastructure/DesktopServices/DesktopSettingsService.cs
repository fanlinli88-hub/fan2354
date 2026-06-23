using WowVmMonitor.App.Settings;
using WowVmMonitor.Infrastructure.Configuration;
using WowVmMonitor.Infrastructure.Credentials;

namespace WowVmMonitor.Infrastructure.DesktopServices;

public sealed class DesktopSettingsService : ISettingsService
{
    private readonly ConfigurationStore _configurationStore;
    private readonly WindowsCredentialStore _credentialStore;

    public DesktopSettingsService(
        ConfigurationStore configurationStore,
        WindowsCredentialStore credentialStore)
    {
        _configurationStore = configurationStore;
        _credentialStore = credentialStore;
    }

    public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var result = _configurationStore.Load();
            return new SettingsLoadResult(
                result.Configuration,
                result.RequiresUserConfirmation,
                result.Messages);
        }, cancellationToken);

    public Task<SettingsSaveResult> SaveAsync(
        SettingsSaveRequest request,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            _configurationStore.Save(request.Configuration);
            foreach (var update in request.CredentialUpdates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _credentialStore.Save(update.MachineId, update.Username, update.Password.Span);
            }

            return new SettingsSaveResult(true, []);
        }, cancellationToken);
}
