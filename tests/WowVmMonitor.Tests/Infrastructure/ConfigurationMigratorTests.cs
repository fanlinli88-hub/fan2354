using WowVmMonitor.Infrastructure.Configuration;

namespace WowVmMonitor.Tests.Infrastructure;

public sealed class ConfigurationMigratorTests
{
    [Fact]
    public void MigratesUnversionedConfigurationToVersionOne()
    {
        const string json = """
            {
              "checkIntervalSeconds": 60,
              "machines": [
                { "name": "VM One", "sharePath": "\\\\server\\wowlogs", "enabled": true }
              ]
            }
            """;

        var result = new ConfigurationMigrator().DeserializeAndMigrate(json);

        Assert.True(result.WasMigrated);
        Assert.Equal(1, result.Configuration.SchemaVersion);
        Assert.Equal("vm-one", result.Configuration.Machines[0].Id);
        Assert.Equal("WowVmMonitor/share/vm-one", result.Configuration.Machines[0].CredentialTarget);
    }

    [Fact]
    public void MigratesNonAsciiNamesToStableSequentialIds()
    {
        const string json = """
            {
              "machines": [
                { "name": "主机一", "sharePath": "\\\\server\\one", "enabled": true },
                { "name": "主机二", "sharePath": "\\\\server\\two", "enabled": true }
              ]
            }
            """;

        var result = new ConfigurationMigrator().DeserializeAndMigrate(json);

        Assert.Equal(["vm-01", "vm-02"], result.Configuration.Machines.Select(machine => machine.Id));
    }

    [Fact]
    public void RejectsUnknownFutureVersion()
    {
        const string json = """{ "schemaVersion": 99, "monitoring": {}, "machines": [] }""";

        var exception = Assert.Throws<ConfigurationFormatException>(
            () => new ConfigurationMigrator().DeserializeAndMigrate(json));

        Assert.Equal("configuration.version.unsupported", exception.Code);
    }
}
