namespace WowVmMonitor.Core.Configuration;

public sealed record ConfigurationMessage(string Code, string Message);

public sealed record ConfigurationLoadResult(
    MonitorConfiguration Configuration,
    bool RequiresUserConfirmation,
    IReadOnlyList<ConfigurationMessage> Messages);
