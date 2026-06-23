using WowVmMonitor.Core.Configuration;

namespace WowVmMonitor.Tests.Core;

public sealed class ConfigurationValidatorTests
{
    [Fact]
    public void AcceptsEightValidMachines()
    {
        var configuration = CreateConfiguration(8);

        var errors = ConfigurationValidator.Validate(configuration);

        Assert.Empty(errors);
    }

    [Fact]
    public void RejectsInvalidMachineShape()
    {
        var source = CreateConfiguration(9);
        var configuration = source with
        {
            Machines = source.Machines
                .Select((machine, index) => index == 1
                    ? machine with
                    {
                        Id = "VM-01",
                        SharePath = "C:\\logs",
                        CredentialTarget = "wrong"
                    }
                    : machine)
                .ToArray()
        };

        var errors = ConfigurationValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Code == "machines.maximum");
        Assert.Contains(errors, error => error.Code == "machines.id.duplicate");
        Assert.Contains(errors, error => error.Code == "machines.sharePath.unc");
        Assert.Contains(errors, error => error.Code == "machines.credentialTarget.invalid");
    }

    [Fact]
    public void PublicMachineConfigurationContainsNoCredentialValues()
    {
        Assert.DoesNotContain(typeof(MachineConfiguration).GetProperties(), property =>
            property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Username", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RejectsInvalidTimingOrder()
    {
        var configuration = CreateConfiguration(1) with
        {
            Monitoring = new MonitoringConfiguration(60, 10, 600, 300)
        };

        Assert.Contains(
            ConfigurationValidator.Validate(configuration),
            error => error.Code == "monitoring.thresholds.order");
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("contains/slash")]
    public void RejectsInvalidNtfyTopic(string topic)
    {
        var configuration = CreateConfiguration(1) with
        {
            Ntfy = new NtfyConfiguration(true, topic)
        };

        Assert.Contains(
            ConfigurationValidator.Validate(configuration),
            error => error.Code == "ntfy.topic.invalid");
    }

    private static MonitorConfiguration CreateConfiguration(int count) =>
        new(
            MonitorConfiguration.CurrentSchemaVersion,
            new MonitoringConfiguration(60, 10, 300, 600),
            Enumerable.Range(1, count)
                .Select(number => new MachineConfiguration(
                    $"vm-{number:D2}",
                    $"VM {number:D2}",
                    $@"\\192.168.1.{100 + number}\wowlogs",
                    true,
                    $"WowVmMonitor/share/vm-{number:D2}"))
                .ToArray());
}
