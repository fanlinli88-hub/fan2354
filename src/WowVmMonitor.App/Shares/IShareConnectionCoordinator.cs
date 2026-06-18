using WowVmMonitor.Core.Configuration;

namespace WowVmMonitor.App.Shares;

public interface IShareConnectionCoordinator
{
    Task<IReadOnlyList<ShareConnectionResult>> ConnectEnabledAsync(
        MonitorConfiguration configuration,
        CancellationToken cancellationToken);
}
