using WowVmMonitor.App.Settings;
using WowVmMonitor.Core.Configuration;

namespace WowVmMonitor.Tests.App;

public sealed class SettingsViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasswordBufferIsClearedAfterSave(bool failSave)
    {
        var service = new RecordingSettingsService(failSave);
        var viewModel = await SettingsViewModel.CreateAsync(service, CancellationToken.None);
        var draft = viewModel.Machines.Single();
        draft.Username = "share-user";
        draft.SetReplacementPassword("unique-ui-password".ToCharArray());
        var observed = draft.ReplacementPassword;

        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask;

        Assert.True(observed.Span.ToArray().All(character => character == '\0'));
        Assert.DoesNotContain("unique-ui-password", viewModel.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlankPasswordDoesNotCreateCredentialUpdate()
    {
        var service = new RecordingSettingsService(failSave: false);
        var viewModel = await SettingsViewModel.CreateAsync(service, CancellationToken.None);

        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask;

        Assert.Equal(0, service.LastCredentialUpdateCount);
    }

    private sealed class RecordingSettingsService(bool failSave) : ISettingsService
    {
        public int LastCredentialUpdateCount { get; private set; }

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SettingsLoadResult(CreateConfiguration(), false, []));

        public Task<SettingsSaveResult> SaveAsync(
            SettingsSaveRequest request,
            CancellationToken cancellationToken)
        {
            LastCredentialUpdateCount = request.CredentialUpdates.Count;
            return failSave
                ? Task.FromException<SettingsSaveResult>(new IOException("Injected save failure."))
                : Task.FromResult(new SettingsSaveResult(true, []));
        }

        private static MonitorConfiguration CreateConfiguration() =>
            new(
                1,
                new MonitoringConfiguration(60, 10, 300, 600),
                [new MachineConfiguration(
                    "vm-01",
                    "VM 01",
                    @"\\server\share",
                    true,
                    "WowVmMonitor/share/vm-01")]);
    }
}
