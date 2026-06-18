using System.Text;
using System.Text.Json;
using WowVmMonitor.Core.Configuration;

namespace WowVmMonitor.Infrastructure.Configuration;

public sealed record ConfigurationMigrationResult(
    MonitorConfiguration Configuration,
    bool WasMigrated,
    int SourceVersion);

public sealed class ConfigurationFormatException : Exception
{
    public ConfigurationFormatException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class ConfigurationMigrator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ConfigurationMigrationResult DeserializeAndMigrate(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ConfigurationFormatException(
                    "configuration.root.invalid",
                    "Configuration root must be a JSON object.");
            }

            if (!document.RootElement.TryGetProperty("schemaVersion", out var versionElement))
            {
                return new ConfigurationMigrationResult(MigrateV0(json), true, 0);
            }

            if (!versionElement.TryGetInt32(out var version) ||
                version != MonitorConfiguration.CurrentSchemaVersion)
            {
                throw new ConfigurationFormatException(
                    "configuration.version.unsupported",
                    "Configuration version is unsupported.");
            }

            var configuration = JsonSerializer.Deserialize<MonitorConfiguration>(json, JsonOptions)
                ?? throw new ConfigurationFormatException(
                    "configuration.empty",
                    "Configuration could not be read.");
            Validate(configuration);
            return new ConfigurationMigrationResult(configuration, false, version);
        }
        catch (ConfigurationFormatException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ConfigurationFormatException(
                "configuration.json.invalid",
                "Configuration JSON is invalid.",
                exception);
        }
    }

    private static MonitorConfiguration MigrateV0(string json)
    {
        var legacy = JsonSerializer.Deserialize<LegacyConfiguration>(json, JsonOptions)
            ?? new LegacyConfiguration();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var machines = (legacy.Machines ?? [])
            .Select((machine, index) => MigrateMachine(machine, index, usedIds))
            .ToArray();
        var configuration = new MonitorConfiguration(
            MonitorConfiguration.CurrentSchemaVersion,
            new MonitoringConfiguration(
                legacy.CheckIntervalSeconds > 0 ? legacy.CheckIntervalSeconds : 60,
                legacy.CheckTimeoutSeconds > 0 ? legacy.CheckTimeoutSeconds : 10,
                legacy.WarningAfterSeconds > 0 ? legacy.WarningAfterSeconds : 300,
                legacy.AlertAfterSeconds > 0 ? legacy.AlertAfterSeconds : 600),
            machines);
        Validate(configuration);
        return configuration;
    }

    private static MachineConfiguration MigrateMachine(
        LegacyMachine machine,
        int index,
        ISet<string> usedIds)
    {
        var displayName = string.IsNullOrWhiteSpace(machine.Name)
            ? $"VM {index + 1:D2}"
            : machine.Name.Trim();
        var baseId = Slugify(displayName);
        if (baseId.Length == 0)
        {
            baseId = $"vm-{index + 1:D2}";
        }

        var id = baseId;
        var suffix = 2;
        while (!usedIds.Add(id))
        {
            id = $"{baseId}-{suffix++}";
        }

        return new MachineConfiguration(
            id,
            displayName,
            machine.SharePath?.Trim() ?? string.Empty,
            machine.Enabled,
            MachineConfiguration.CredentialTargetFor(id));
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = false;
        foreach (var character in value.ToLowerInvariant())
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static void Validate(MonitorConfiguration configuration)
    {
        var errors = ConfigurationValidator.Validate(configuration);
        if (errors.Count > 0)
        {
            throw new ConfigurationFormatException(errors[0].Code, errors[0].Message);
        }
    }

    private sealed class LegacyConfiguration
    {
        public int CheckIntervalSeconds { get; init; }

        public int CheckTimeoutSeconds { get; init; }

        public int WarningAfterSeconds { get; init; }

        public int AlertAfterSeconds { get; init; }

        public IReadOnlyList<LegacyMachine>? Machines { get; init; }
    }

    private sealed class LegacyMachine
    {
        public string? Name { get; init; }

        public string? SharePath { get; init; }

        public bool Enabled { get; init; }
    }
}
