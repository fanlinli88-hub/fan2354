using System.Diagnostics;
using WowVmMonitor.Infrastructure.Logs;

namespace WowVmMonitor.Tests.Infrastructure;

public sealed class LatestLogLocatorTests
{
    [Fact]
    public void SelectsNewestLogAcrossMultipleCharacters()
    {
        using var fixture = new LogDirectoryFixture();
        fixture.CreateLog("character-a", "2026-06-18", "10-0.log", Utc(10, 30));
        var expected = fixture.CreateLog("character-b", "2026-06-18", "10-0.log", Utc(10, 31));

        var result = new LatestLogLocator().FindLatest(fixture.RootPath);

        Assert.Equal(expected, result?.FullPath);
    }

    [Fact]
    public void UsesLatestDateDirectoryForEachCharacter()
    {
        using var fixture = new LogDirectoryFixture();
        fixture.CreateLog("character-a", "2026-06-17", "23-0.log", Utc(12, 0));
        var expected = fixture.CreateLog("character-a", "2026-06-18", "00-0.log", Utc(0, 1));

        var result = new LatestLogLocator().FindLatest(fixture.RootPath);

        Assert.Equal(expected, result?.FullPath);
    }

    [Fact]
    public void SelectsNewestHourlyLogWithinLatestDate()
    {
        using var fixture = new LogDirectoryFixture();
        fixture.CreateLog("character-a", "2026-06-18", "10-0.log", Utc(10, 59));
        var expected = fixture.CreateLog("character-a", "2026-06-18", "11-0.log", Utc(11, 1));

        var result = new LatestLogLocator().FindLatest(fixture.RootPath);

        Assert.Equal(expected, result?.FullPath);
    }

    [Fact]
    public void ReturnsNullForEmptyDirectory()
    {
        using var fixture = new LogDirectoryFixture();

        var result = new LatestLogLocator().FindLatest(fixture.RootPath);

        Assert.Null(result);
    }

    [Fact]
    public void MissingCachedFileTriggersFreshDiscovery()
    {
        using var fixture = new LogDirectoryFixture();
        var fallback = fixture.CreateLog("character-a", "2026-06-18", "10-0.log", Utc(10, 0));
        var cached = fixture.CreateLog("character-a", "2026-06-18", "11-0.log", Utc(11, 0));
        File.Delete(cached);

        var result = new LatestLogLocator().FindLatest(fixture.RootPath, cached);

        Assert.Equal(fallback, result?.FullPath);
        Assert.False(result?.FromCache);
    }

    [Fact]
    public void ExistingCachedFileAvoidsDirectoryRefresh()
    {
        using var fixture = new LogDirectoryFixture();
        var cached = fixture.CreateLog("character-a", "2026-06-18", "10-0.log", Utc(10, 0));
        fixture.CreateLog("character-a", "2026-06-18", "11-0.log", Utc(11, 0));

        var result = new LatestLogLocator().FindLatest(fixture.RootPath, cached);

        Assert.Equal(cached, result?.FullPath);
        Assert.True(result?.FromCache);
    }

    [Fact]
    public void ForceRefreshFindsNewlyCreatedLog()
    {
        using var fixture = new LogDirectoryFixture();
        var cached = fixture.CreateLog("character-a", "2026-06-18", "10-0.log", Utc(10, 0));
        var expected = fixture.CreateLog("character-a", "2026-06-18", "11-0.log", Utc(11, 0));

        var result = new LatestLogLocator().FindLatest(fixture.RootPath, cached, forceRefresh: true);

        Assert.Equal(expected, result?.FullPath);
        Assert.False(result?.FromCache);
    }

    [Fact]
    public void SupportsChineseCharacterDirectoryNames()
    {
        using var fixture = new LogDirectoryFixture();
        var expected = fixture.CreateLog("铁血勇士@服务器", "2026-06-18", "12-0.log", Utc(12, 1));

        var result = new LatestLogLocator().FindLatest(fixture.RootPath);

        Assert.Equal(expected, result?.FullPath);
    }

    [Fact]
    public void DoesNotRecursivelyScanBelowDateDirectory()
    {
        using var fixture = new LogDirectoryFixture();
        var expected = fixture.CreateLog("character-a", "2026-06-18", "12-0.log", Utc(12, 0));
        fixture.CreateNestedLog("character-a", "2026-06-18", "archive", "future.log", Utc(23, 0));

        var result = new LatestLogLocator().FindLatest(fixture.RootPath);

        Assert.Equal(expected, result?.FullPath);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ElevenCharacterDiscoveryCompletesWithinOneSecond()
    {
        using var fixture = new LogDirectoryFixture();
        for (var character = 1; character <= 11; character++)
        {
            for (var day = 1; day <= 30; day++)
            {
                Directory.CreateDirectory(Path.Combine(
                    fixture.RootPath,
                    $"character-{character:00}",
                    $"2026-05-{day:00}"));
            }

            for (var hour = 0; hour < 24; hour++)
            {
                fixture.CreateLog(
                    $"character-{character:00}",
                    "2026-06-18",
                    $"{hour:00}-0.log",
                    Utc(hour, character));
            }
        }

        var stopwatch = Stopwatch.StartNew();
        var result = new LatestLogLocator().FindLatest(fixture.RootPath);
        stopwatch.Stop();

        Assert.NotNull(result);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"Discovery took {stopwatch.Elapsed.TotalMilliseconds:N0} ms.");
    }

    private static DateTimeOffset Utc(int hour, int minute) =>
        new(2026, 6, 18, hour, minute, 0, TimeSpan.Zero);

    private sealed class LogDirectoryFixture : IDisposable
    {
        public LogDirectoryFixture()
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

        public string CreateNestedLog(
            string character,
            string date,
            string nestedDirectory,
            string fileName,
            DateTimeOffset lastWriteTime)
        {
            var directory = Path.Combine(RootPath, character, date, nestedDirectory);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, "archive");
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
