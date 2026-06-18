using System.Globalization;

namespace WowVmMonitor.Infrastructure.Logs;

public sealed class LatestLogLocator
{
    private const string DateDirectoryFormat = "yyyy-MM-dd";

    public LatestLogFile? FindLatest(
        string rootPath,
        string? cachedFilePath = null,
        bool forceRefresh = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var normalizedRoot = Path.GetFullPath(rootPath);
        if (!forceRefresh && IsUsableCachedFile(normalizedRoot, cachedFilePath))
        {
            var cachedResult = TryCreateResult(cachedFilePath!, fromCache: true);
            if (cachedResult is not null)
            {
                return cachedResult;
            }
        }

        if (!Directory.Exists(normalizedRoot))
        {
            throw new DirectoryNotFoundException($"Log share was not found: {normalizedRoot}");
        }

        LatestLogFile? latest = null;
        foreach (var characterDirectory in Directory.EnumerateDirectories(
                     normalizedRoot,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var latestDateDirectory = FindLatestDateDirectory(characterDirectory);
            if (latestDateDirectory is null)
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(
                         latestDateDirectory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                if (!filePath.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = TryCreateResult(filePath, fromCache: false);
                if (candidate is not null &&
                    (latest is null || candidate.LastWriteTime > latest.LastWriteTime))
                {
                    latest = candidate;
                }
            }
        }

        return latest;
    }

    private static string? FindLatestDateDirectory(string characterDirectory)
    {
        DateOnly? latestDate = null;
        string? latestPath = null;

        foreach (var directory in Directory.EnumerateDirectories(
                     characterDirectory,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(directory);
            if (!DateOnly.TryParseExact(
                    name,
                    DateDirectoryFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                continue;
            }

            if (latestDate is null || date > latestDate.Value)
            {
                latestDate = date;
                latestPath = directory;
            }
        }

        return latestPath;
    }

    private static bool IsUsableCachedFile(string normalizedRoot, string? cachedFilePath)
    {
        if (string.IsNullOrWhiteSpace(cachedFilePath) || !File.Exists(cachedFilePath))
        {
            return false;
        }

        var normalizedCachedPath = Path.GetFullPath(cachedFilePath);
        var rootPrefix = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedCachedPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static LatestLogFile CreateResult(string filePath, bool fromCache)
    {
        var lastWriteTime = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero);
        return new LatestLogFile(Path.GetFullPath(filePath), lastWriteTime, fromCache);
    }

    private static LatestLogFile? TryCreateResult(string filePath, bool fromCache)
    {
        try
        {
            return CreateResult(filePath, fromCache);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
