using System.Diagnostics;
using WowVmMonitor.App.Notifications;
using WowVmMonitor.Core.Configuration;
using WowVmMonitor.Infrastructure.Notifications;

namespace WowVmMonitor.Tests.Infrastructure;

public sealed class NtfyNotificationDispatcherTests
{
    [Fact]
    public async Task EnqueueDoesNotWaitForBlockedNetwork()
    {
        var sender = new BlockingSender();
        await using var dispatcher = new NtfyNotificationDispatcher(sender, capacity: 4);
        dispatcher.Configure(new NtfyConfiguration(true, "topic-one"));

        var stopwatch = Stopwatch.StartNew();
        Assert.True(dispatcher.TryEnqueue(CreateNotification("VM 01")));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100));
        await sender.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        sender.Release.TrySetResult();
    }

    [Fact]
    public async Task DisabledConfigurationRejectsDelivery()
    {
        var sender = new BlockingSender();
        await using var dispatcher = new NtfyNotificationDispatcher(sender, capacity: 2);
        dispatcher.Configure(new NtfyConfiguration(false, "topic-one"));

        Assert.False(dispatcher.TryEnqueue(CreateNotification("VM 01")));
        Assert.Equal(0, sender.SendCount);
    }

    [Fact]
    public async Task FullQueueDropsOldestPendingNotification()
    {
        var sender = new BlockingSender();
        await using var dispatcher = new NtfyNotificationDispatcher(sender, capacity: 1);
        dispatcher.Configure(new NtfyConfiguration(true, "topic-one"));

        Assert.True(dispatcher.TryEnqueue(CreateNotification("VM 01")));
        await sender.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(dispatcher.TryEnqueue(CreateNotification("VM 02")));
        Assert.True(dispatcher.TryEnqueue(CreateNotification("VM 03")));

        Assert.Equal(1, dispatcher.DroppedCount);
        sender.Release.TrySetResult();
    }

    private static NtfyNotification CreateNotification(string machine) =>
        new(NtfyNotificationKind.Alert, machine, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10));

    private sealed class BlockingSender : INtfySender
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SendCount { get; private set; }

        public async Task<NtfySendResult> SendAsync(
            NtfyNotification notification,
            string topic,
            CancellationToken cancellationToken)
        {
            SendCount++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return NtfySendResult.Success();
        }
    }
}
