using WowVmMonitor.Core.Configuration;

namespace WowVmMonitor.Infrastructure.Shares;

public interface IShareConnectionCoordinator
{
    Task<IReadOnlyList<ShareConnectionResult>> ConnectEnabledAsync(
        MonitorConfiguration configuration,
        CancellationToken cancellationToken);
}
