using System.Threading.Channels;
using WowVmMonitor.App.Notifications;
using WowVmMonitor.Core.Configuration;

namespace WowVmMonitor.Infrastructure.Notifications;

public sealed class NtfyNotificationDispatcher : INotificationDispatcher, IAsyncDisposable
{
    private readonly INtfySender _sender;
    private readonly Channel<QueuedNotification> _channel;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _writeLock = new();
    private readonly Task _worker;
    private NtfyConfiguration _configuration = NtfyConfiguration.CreateDefault();
    private long _droppedCount;
    private string? _lastFailureCode;
    private int _disposed;

    public NtfyNotificationDispatcher(INtfySender sender, int capacity = 100)
    {
        ArgumentNullException.ThrowIfNull(sender);
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _sender = sender;
        _channel = Channel.CreateBounded<QueuedNotification>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
        _worker = RunAsync(_cancellation.Token);
    }

    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    public string? LastFailureCode => Volatile.Read(ref _lastFailureCode);

    public void Configure(NtfyConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Volatile.Write(ref _configuration, configuration);
    }

    public bool TryEnqueue(NtfyNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var configuration = Volatile.Read(ref _configuration);
        if (!configuration.Enabled || Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        var queued = new QueuedNotification(notification, configuration.Topic);
        lock (_writeLock)
        {
            if (_channel.Writer.TryWrite(queued))
            {
                return true;
            }

            if (_channel.Reader.TryRead(out _))
            {
                Interlocked.Increment(ref _droppedCount);
            }

            return _channel.Writer.TryWrite(queued);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        try
        {
            await _worker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _cancellation.Cancel();
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
            }
        }
        finally
        {
            _cancellation.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var queued in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var result = await _sender
                .SendAsync(queued.Notification, queued.Topic, cancellationToken)
                .ConfigureAwait(false);
            Volatile.Write(ref _lastFailureCode, result.FailureCode);
        }
    }

    private sealed record QueuedNotification(NtfyNotification Notification, string Topic);
}
