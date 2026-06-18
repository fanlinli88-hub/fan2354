using WowVmMonitor.App;
using WowVmMonitor.App.Shares;
using WowVmMonitor.Core.Configuration;

namespace WowVmMonitor.Tests.App;

public sealed class StartupConfigurationTests
{
    [Fact]
    public async Task ConfirmationRequiredConfigurationDoesNotConnectShares()
    {
        var connector = new RecordingShareConnector();
        var result = new ConfigurationLoadResult(
            MonitorConfiguration.CreateDefault(),
            true,
            [new ConfigurationMessage(
                "configuration.default.confirmationRequired",
                "Review configuration.")]);

        var exitCode = await StartupRunner.RunAsync(
            result,
            connector,
            TextWriter.Null,
            CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, connector.CallCount);
    }

    [Fact]
    public async Task ValidConfigurationConnectsEnabledShares()
    {
        var connector = new RecordingShareConnector();
        var result = new ConfigurationLoadResult(CreateConfiguration(), false, []);

        var exitCode = await StartupRunner.RunAsync(
            result,
            connector,
            TextWriter.Null,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, connector.CallCount);
    }

    private static MonitorConfiguration CreateConfiguration() =>
        new(
            1,
            new MonitoringConfiguration(60, 10, 300, 600),
            [new MachineConfiguration(
                "vm-01",
                "VM 01",
                @"\\server\wowlogs",
                true,
                "WowVmMonitor/share/vm-01")]);

    private sealed class RecordingShareConnector : IShareConnectionCoordinator
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ShareConnectionResult>> ConnectEnabledAsync(
            MonitorConfiguration configuration,
            CancellationToken cancellationToken)
        {
            CallCount++;
            IReadOnlyList<ShareConnectionResult> results = configuration.Machines
                .Where(machine => machine.Enabled)
                .Select(machine => new ShareConnectionResult(
                    machine.Id,
                    machine.SharePath,
                    true,
                    "share.connected",
                    null))
                .ToArray();
            return Task.FromResult(results);
        }
    }
}
