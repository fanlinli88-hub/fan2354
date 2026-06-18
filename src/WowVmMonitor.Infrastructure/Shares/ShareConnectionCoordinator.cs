using WowVmMonitor.Core.Configuration;
using WowVmMonitor.App.Shares;
using WowVmMonitor.Infrastructure.Credentials;

namespace WowVmMonitor.Infrastructure.Shares;

public sealed class ShareConnectionCoordinator : IShareConnectionCoordinator
{
    private readonly IShareCredentialStore _credentialStore;
    private readonly IWindowsNetworkApi _networkApi;

    public ShareConnectionCoordinator(
        IShareCredentialStore credentialStore,
        IWindowsNetworkApi networkApi)
    {
        ArgumentNullException.ThrowIfNull(credentialStore);
        ArgumentNullException.ThrowIfNull(networkApi);
        _credentialStore = credentialStore;
        _networkApi = networkApi;
    }

    public async Task<IReadOnlyList<ShareConnectionResult>> ConnectEnabledAsync(
        MonitorConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var validationErrors = ConfigurationValidator.Validate(configuration);
        if (validationErrors.Count > 0)
        {
            throw new ArgumentException(validationErrors[0].Message, nameof(configuration));
        }

        var tasks = configuration.Machines
            .Where(machine => machine.Enabled)
            .Select(machine => Task.Run(() => ConnectOne(machine, cancellationToken), cancellationToken))
            .ToArray();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private ShareConnectionResult ConnectOne(
        MachineConfiguration machine,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var credential = _credentialStore.Read(machine.Id);
            if (credential is null)
            {
                return Failure(machine, "credential.missing", null);
            }

            var errorCode = _networkApi.Connect(
                machine.SharePath,
                credential.Username,
                credential.Password);
            return errorCode == 0
                ? new ShareConnectionResult(machine.Id, machine.SharePath, true, "share.connected", null)
                : Failure(machine, "share.connect.failed", errorCode);
        }
        catch (CredentialStoreException exception)
        {
            return Failure(machine, "credential.read.failed", exception.Win32ErrorCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Failure(machine, "share.connect.error", null);
        }
    }

    private static ShareConnectionResult Failure(
        MachineConfiguration machine,
        string code,
        int? win32ErrorCode) =>
        new(machine.Id, machine.SharePath, false, code, win32ErrorCode);
}
