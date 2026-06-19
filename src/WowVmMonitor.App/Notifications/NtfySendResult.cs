namespace WowVmMonitor.App.Notifications;

public sealed record NtfySendResult(bool Succeeded, string? FailureCode)
{
    public static NtfySendResult Success() => new(true, null);
    public static NtfySendResult Failure(string code) => new(false, code);
}
