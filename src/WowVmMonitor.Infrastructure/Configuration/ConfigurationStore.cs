using System.Text.Json;
using WowVmMonitor.Core.Configuration;

namespace WowVmMonitor.Infrastructure.Configuration;

public sealed class ConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _directory;
    private readonly string _primaryPath;
    private readonly string _backupPath;
    private readonly string _temporaryPath;
    private readonly ConfigurationMigrator _migrator;
    private readonly IConfigurationFileOperations _files;

    public ConfigurationStore(
        string directory,
        ConfigurationMigrator? migrator = null,
        IConfigurationFileOperations? fileOperations = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _primaryPath = Path.Combine(directory, "config.json");
        _backupPath = Path.Combine(directory, "config.json.bak");
        _temporaryPath = Path.Combine(directory, "config.json.tmp");
        _migrator = migrator ?? new ConfigurationMigrator();
        _files = fileOperations ?? new SystemConfigurationFileOperations();
    }

    public ConfigurationLoadResult Load()
    {
        var primary = TryLoad(_primaryPath);
        if (primary is not null)
        {
            if (primary.WasMigrated)
            {
                Save(primary.Configuration);
                return new ConfigurationLoadResult(
                    primary.Configuration,
                    false,
                    [new ConfigurationMessage(
                        "configuration.migrated.v0-v1",
                        "Configuration was upgraded from version 0 to version 1.")]);
            }

            return new ConfigurationLoadResult(primary.Configuration, false, []);
        }

        PreserveCorrupt(_primaryPath);
        var backup = TryLoad(_backupPath);
        if (backup is not null)
        {
            Save(backup.Configuration);
            return new ConfigurationLoadResult(
                backup.Configuration,
                false,
                [new ConfigurationMessage(
                    "configuration.recovered.backup",
                    "The primary configuration was invalid and has been restored from backup.")]);
        }

        PreserveCorrupt(_backupPath);
        var defaultConfiguration = MonitorConfiguration.CreateDefault();
        Save(defaultConfiguration);
        return new ConfigurationLoadResult(
            defaultConfiguration,
            true,
            [new ConfigurationMessage(
                "configuration.default.confirmationRequired",
                "No valid configuration was available. Review the generated default before monitoring.")]);
    }

    public void Save(MonitorConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var errors = ConfigurationValidator.Validate(configuration);
        if (errors.Count > 0)
        {
            throw new ConfigurationFormatException(errors[0].Code, errors[0].Message);
        }

        _files.CreateDirectory(_directory);
        try
        {
            var json = JsonSerializer.Serialize(configuration, JsonOptions);
            _files.WriteAllTextAndFlush(_temporaryPath, json);
            _ = _migrator.DeserializeAndMigrate(_files.ReadAllText(_temporaryPath));

            if (_files.Exists(_primaryPath))
            {
                _files.Replace(_temporaryPath, _primaryPath, _backupPath);
            }
            else
            {
                _files.Move(_temporaryPath, _primaryPath);
            }
        }
        finally
        {
            if (_files.Exists(_temporaryPath))
            {
                _files.Delete(_temporaryPath);
            }
        }
    }

    private ConfigurationMigrationResult? TryLoad(string path)
    {
        if (!_files.Exists(path))
        {
            return null;
        }

        try
        {
            return _migrator.DeserializeAndMigrate(_files.ReadAllText(path));
        }
        catch (Exception exception) when (exception is ConfigurationFormatException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void PreserveCorrupt(string path)
    {
        if (!_files.Exists(path))
        {
            return;
        }

        var fileName = Path.GetFileName(path);
        var stem = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^5]
            : fileName;
        var timestamp = _files.UtcNow.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'");
        var corruptPath = Path.Combine(_directory, $"{stem}.corrupt-{timestamp}.json");
        _files.Move(path, corruptPath);
    }
}
