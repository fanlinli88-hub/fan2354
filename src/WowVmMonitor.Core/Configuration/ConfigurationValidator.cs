namespace WowVmMonitor.Core.Configuration;

public sealed record ConfigurationValidationError(string Code, string Message);

public static class ConfigurationValidator
{
    public const int MaximumMachineCount = 8;

    public static IReadOnlyList<ConfigurationValidationError> Validate(
        MonitorConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var errors = new List<ConfigurationValidationError>();

        if (configuration.SchemaVersion != MonitorConfiguration.CurrentSchemaVersion)
        {
            errors.Add(new("configuration.version.unsupported", "Configuration version is unsupported."));
        }

        ValidateMonitoring(configuration.Monitoring, errors);
        ValidateMachines(configuration.Machines, errors);
        ValidateNtfy(configuration.Ntfy, errors);
        return errors;
    }

    private static void ValidateNtfy(
        NtfyConfiguration? ntfy,
        ICollection<ConfigurationValidationError> errors)
    {
        if (ntfy is null || !NtfyConfiguration.IsValidTopic(ntfy.Topic))
        {
            errors.Add(new("ntfy.topic.invalid", "ntfy topic must contain 1-64 letters, numbers, underscores, or hyphens."));
        }
    }

    private static void ValidateMonitoring(
        MonitoringConfiguration? monitoring,
        ICollection<ConfigurationValidationError> errors)
    {
        if (monitoring is null)
        {
            errors.Add(new("monitoring.required", "Monitoring settings are required."));
            return;
        }

        if (monitoring.CheckIntervalSeconds <= 0 || monitoring.CheckTimeoutSeconds <= 0 ||
            monitoring.WarningAfterSeconds <= 0 || monitoring.AlertAfterSeconds <= 0)
        {
            errors.Add(new("monitoring.values.positive", "Monitoring values must be positive."));
        }

        if (monitoring.AlertAfterSeconds <= monitoring.WarningAfterSeconds)
        {
            errors.Add(new("monitoring.thresholds.order", "Alert threshold must exceed warning threshold."));
        }
    }

    private static void ValidateMachines(
        IReadOnlyList<MachineConfiguration>? machines,
        ICollection<ConfigurationValidationError> errors)
    {
        if (machines is null)
        {
            errors.Add(new("machines.required", "Machine configuration is required."));
            return;
        }

        if (machines.Count > MaximumMachineCount)
        {
            errors.Add(new("machines.maximum", "No more than eight machines may be configured."));
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var machine in machines)
        {
            if (string.IsNullOrWhiteSpace(machine.Id))
            {
                errors.Add(new("machines.id.required", "Machine ID is required."));
            }
            else if (!ids.Add(machine.Id))
            {
                errors.Add(new("machines.id.duplicate", "Machine IDs must be unique."));
            }

            if (string.IsNullOrWhiteSpace(machine.DisplayName))
            {
                errors.Add(new("machines.displayName.required", "Machine display name is required."));
            }
            else if (!names.Add(machine.DisplayName))
            {
                errors.Add(new("machines.displayName.duplicate", "Machine display names must be unique."));
            }

            if (machine.Enabled &&
                (string.IsNullOrWhiteSpace(machine.SharePath) || !machine.SharePath.StartsWith(@"\\", StringComparison.Ordinal)))
            {
                errors.Add(new("machines.sharePath.unc", "Enabled machines require a UNC share path."));
            }

            if (string.IsNullOrWhiteSpace(machine.Id) ||
                !string.Equals(
                    machine.CredentialTarget,
                    MachineConfiguration.CredentialTargetFor(machine.Id),
                    StringComparison.Ordinal))
            {
                errors.Add(new("machines.credentialTarget.invalid", "Credential target must match the machine ID."));
            }
        }
    }
}
