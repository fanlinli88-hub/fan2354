using System.Text.Json;
using WowVmMonitor.Core.Configuration;
using WowVmMonitor.Infrastructure.Credentials;
using WowVmMonitor.Infrastructure.Shares;

namespace WowVmMonitor.Tests.Infrastructure;

public sealed class ShareConnectionCoordinatorTests
{
    [Fact]
    public async Task OneAuthenticationFailureDoesNotStopOtherSevenConnections()
    {
        var credentials = new StubCredentialStore();
        var network = new StubWindowsNetworkApi(failingSharePath: @"\\192.168.1.101\wowlogs", errorCode: 1326);
        var coordinator = new ShareConnectionCoordinator(credentials, network);

        var results = await coordinator.ConnectEnabledAsync(
            CreateEightMachineConfiguration(),
            CancellationToken.None);

        Assert.Equal(8, results.Count);
        Assert.False(results.Single(result => result.MachineId == "vm-01").Succeeded);
        Assert.All(
            results.Where(result => result.MachineId != "vm-01"),
            result => Assert.True(result.Succeeded));
        Assert.All(credentials.PasswordMemories, password =>
            Assert.True(password.Span.ToArray().All(character => character == '\0')));

        var serialized = JsonSerializer.Serialize(results);
        Assert.DoesNotContain("test-user", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("test-password", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingCredentialReturnsMachineScopedFailure()
    {
        var credentials = new StubCredentialStore(missingMachineId: "vm-03");
        var coordinator = new ShareConnectionCoordinator(credentials, new StubWindowsNetworkApi());

        var results = await coordinator.ConnectEnabledAsync(
            CreateEightMachineConfiguration(),
            CancellationToken.None);

        var missing = Assert.Single(results, result => result.MachineId == "vm-03");
        Assert.Equal("credential.missing", missing.Code);
        Assert.Equal(7, results.Count(result => result.Succeeded));
    }

    private static MonitorConfiguration CreateEightMachineConfiguration() =>
        new(
            1,
            new MonitoringConfiguration(60, 10, 300, 600),
            Enumerable.Range(1, 8)
                .Select(number => new MachineConfiguration(
                    $"vm-{number:D2}",
                    $"VM {number:D2}",
                    $@"\\192.168.1.{100 + number}\wowlogs",
                    true,
                    $"WowVmMonitor/share/vm-{number:D2}"))
                .ToArray());

    private sealed class StubCredentialStore(string? missingMachineId = null) : IShareCredentialStore
    {
        public List<ReadOnlyMemory<char>> PasswordMemories { get; } = [];

        public ShareCredential? Read(string machineId)
        {
            if (machineId == missingMachineId)
            {
                return null;
            }

            var credential = new ShareCredential("test-user", "test-password".ToCharArray());
            PasswordMemories.Add(credential.Password);
            return credential;
        }
    }

    private sealed class StubWindowsNetworkApi(
        string? failingSharePath = null,
        int errorCode = 0) : IWindowsNetworkApi
    {
        public int Connect(string sharePath, string username, ReadOnlyMemory<char> password) =>
            string.Equals(sharePath, failingSharePath, StringComparison.OrdinalIgnoreCase)
                ? errorCode
                : 0;
    }
}
