using WowVmMonitor.Core.Monitoring;
using WowVmMonitor.Infrastructure.Logs;

namespace WowVmMonitor.Tests.Infrastructure;

public sealed class SharedLogActivitySourceTests
{
    [Fact]
    public async Task ReadsLatestLogFromRealDirectoryStructure()
    {
        using var fixture = new SharedLogFixture();
        var expected = fixture.CreateLog("角色甲", "2026-06-18", "12-0.log", Utc(12, 0));
        var source = CreateSource(fixture.RootPath, Utc(12, 1));

        var result = await source.ReadLatestAsync(CancellationToken.None);

        Assert.Equal(LogActivityReadStatus.Available, result.Status);
        Assert.Equal(expected, result.Snapshot?.FullPath);
        Assert.Equal(Utc(12, 0), result.Snapshot?.LastWriteTime);
    }

    [Fact]
    public async Task MissingShareReturnsUnavailableInsteadOfThrowing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var source = CreateSource(missingPath, Utc(12, 0));

        var result = await source.ReadLatestAsync(CancellationToken.None);

        Assert.Equal(LogActivityReadStatus.ShareUnavailable, result.Status);
        Assert.NotEmpty(result.ErrorMessage ?? string.Empty);
    }

    [Fact]
    public async Task EmptyShareReturnsNoLog()
    {
        using var fixture = new SharedLogFixture();
        var source = CreateSource(fixture.RootPath, Utc(12, 0));

        var result = await source.ReadLatestAsync(CancellationToken.None);

        Assert.Equal(LogActivityReadStatus.NoLog, result.Status);
    }

    [Fact]
    public async Task StaleCacheRefreshesToNewLog()
    {
        using var fixture = new SharedLogFixture();
        var clock = new StubTimeProvider(Utc(12, 1));
        var first = fixture.CreateLog("角色甲", "2026-06-18", "12-0.log", Utc(12, 0));
        var source = new SharedLogActivitySource(
            fixture.RootPath,
            new LatestLogLocator(),
            TimeSpan.FromMinutes(5),
            clock);

        var initial = await source.ReadLatestAsync(CancellationToken.None);
        var second = fixture.CreateLog("角色甲", "2026-06-18", "13-0.log", Utc(13, 0));
        clock.SetUtcNow(Utc(13, 1));
        var refreshed = await source.ReadLatestAsync(CancellationToken.None);

        Assert.Equal(first, initial.Snapshot?.FullPath);
        Assert.Equal(second, refreshed.Snapshot?.FullPath);
    }

    [Fact]
    public async Task HonorsCancellationBeforeFileAccess()
    {
        using var fixture = new SharedLogFixture();
        var source = CreateSource(fixture.RootPath, Utc(12, 0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await source.ReadLatestAsync(cancellation.Token));
    }

    private static SharedLogActivitySource CreateSource(string path, DateTimeOffset now) =>
        new(
            path,
            new LatestLogLocator(),
            TimeSpan.FromMinutes(5),
            new StubTimeProvider(now));

    private static DateTimeOffset Utc(int hour, int minute) =>
        new(2026, 6, 18, hour, minute, 0, TimeSpan.Zero);

    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }

    private sealed class SharedLogFixture : IDisposable
    {
        public SharedLogFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "WowVmMonitor.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string CreateLog(
            string character,
            string date,
            string fileName,
            DateTimeOffset lastWriteTime)
        {
            var directory = Path.Combine(RootPath, character, date);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, "activity");
            File.SetLastWriteTimeUtc(path, lastWriteTime.UtcDateTime);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
