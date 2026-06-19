using System.Text.Json;
using WowVmMonitor.App.Shares;
using WowVmMonitor.Core.Configuration;
using WowVmMonitor.Infrastructure.Credentials;

namespace WowVmMonitor.Tests.Security;

public sealed class SecretLeakageTests
{
    [Fact]
    public void PublicConfigurationAndErrorsNeverContainCredentialValues()
    {
        const string username = "unique-user-security-test";
        const string password = "unique-password-security-test";
        var configuration = new MonitorConfiguration(
            MonitorConfiguration.CurrentSchemaVersion,
            new MonitoringConfiguration(60, 10, 300, 600),
            [new MachineConfiguration(
                "vm-01",
                "VM 01",
                @"\\server\share",
                true,
                "WowVmMonitor/share/vm-01")]);
        var connectionError = new ShareConnectionResult(
            "vm-01",
            @"\\server\share",
            false,
            "share.connect.failed",
            1326);
        var credentialError = new CredentialStoreException(
            "read",
            "WowVmMonitor/share/vm-01",
            1168);

        var publicText = JsonSerializer.Serialize(configuration) +
                         JsonSerializer.Serialize(connectionError) +
                         credentialError;

        Assert.DoesNotContain(username, publicText, StringComparison.Ordinal);
        Assert.DoesNotContain(password, publicText, StringComparison.Ordinal);
    }
}
