namespace WowVmMonitor.App.Shares;

public sealed record ShareConnectionResult(
    string MachineId,
    string SharePath,
    bool Succeeded,
    string Code,
    int? Win32ErrorCode);
