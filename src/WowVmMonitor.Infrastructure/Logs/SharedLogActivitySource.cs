using WowVmMonitor.Core.Monitoring;

namespace WowVmMonitor.Infrastructure.Logs;

public sealed class SharedLogActivitySource : ILogActivitySource
{
    private readonly string _rootPath;
    private readonly LatestLogLocator _locator;
    private readonly TimeSpan _refreshCachedFileAfter;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _readLock = new(1, 1);
    private string? _cachedFilePath;
    private LogActivitySnapshot? _cachedSnapshot;

    public SharedLogActivitySource(
        string rootPath,
        LatestLogLocator locator,
        TimeSpan refreshCachedFileAfter,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(locator);

        if (refreshCachedFileAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshCachedFileAfter));
        }

        _rootPath = rootPath;
        _locator = locator;
        _refreshCachedFileAfter = refreshCachedFileAfter;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<LogActivityReadResult> ReadLatestAsync(CancellationToken cancellationToken)
    {
        await _readLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(ReadLatest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsShareAccessException(exception))
        {
            return LogActivityReadResult.ShareUnavailable(exception.Message);
        }
        finally
        {
            _readLock.Release();
        }
    }

    private LogActivityReadResult ReadLatest()
    {
        var forceRefresh = _cachedSnapshot is not null &&
                           _timeProvider.GetUtcNow() - _cachedSnapshot.LastWriteTime >= _refreshCachedFileAfter;

        var latest = _locator.FindLatest(_rootPath, _cachedFilePath, forceRefresh);
        if (latest is null)
        {
            _cachedFilePath = null;
            _cachedSnapshot = null;
            return LogActivityReadResult.NoLog();
        }

        _cachedFilePath = latest.FullPath;
        _cachedSnapshot = new LogActivitySnapshot(latest.FullPath, latest.LastWriteTime);
        return LogActivityReadResult.Available(_cachedSnapshot);
    }

    private static bool IsShareAccessException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;
}
