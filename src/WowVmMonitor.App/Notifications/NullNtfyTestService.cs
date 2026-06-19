namespace WowVmMonitor.App.Notifications;

public sealed class NullNtfyTestService : INtfyTestService
{
    public Task<NtfySendResult> SendTestAsync(string topic, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(NtfySendResult.Failure("ntfy.unavailable"));
    }
}
