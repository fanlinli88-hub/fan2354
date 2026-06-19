namespace WowVmMonitor.Core.Configuration;

public sealed record MachineConfiguration(
    string Id,
    string DisplayName,
    string SharePath,
    bool Enabled,
    string CredentialTarget)
{
    public static string CredentialTargetFor(string machineId) =>
        $"WowVmMonitor/share/{machineId}";
}
