using WowVmMonitor.App.Settings;
using WowVmMonitor.App.Notifications;
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

    [Fact]
    public async Task AddMachineStopsAtEight()
    {
        var viewModel = await SettingsViewModel.CreateAsync(
            new RecordingSettingsService(failSave: false), CancellationToken.None);

        while (viewModel.AddMachineCommand.CanExecute(null))
        {
            viewModel.AddMachineCommand.Execute(null);
        }

        Assert.Equal(8, viewModel.Machines.Count);
        Assert.False(viewModel.AddMachineCommand.CanExecute(null));
        Assert.Equal(8, viewModel.Machines.Select(machine => machine.Id).Distinct().Count());
    }

    [Fact]
    public async Task SuccessfulSaveClearsRequiredConfirmation()
    {
        var viewModel = await SettingsViewModel.CreateAsync(
            new RecordingSettingsService(failSave: false, requiresConfirmation: true),
            CancellationToken.None);

        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask;

        Assert.False(viewModel.RequiresUserConfirmation);
    }

    [Fact]
    public async Task NtfySettingsAreLoadedSavedAndTested()
    {
        var service = new RecordingSettingsService(failSave: false);
        var ntfy = new RecordingNtfyTestService();
        var viewModel = await SettingsViewModel.CreateAsync(service, CancellationToken.None, ntfy);

        Assert.True(viewModel.NtfyEnabled);
        Assert.Equal("wow-vm-85898-fan2354", viewModel.NtfyTopic);
        viewModel.TestNtfyCommand.Execute(null);
        await viewModel.TestNtfyCommand.ExecutionTask;
        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask;

        Assert.Equal("wow-vm-85898-fan2354", ntfy.LastTopic);
        Assert.Equal("测试通知发送成功。", viewModel.NtfyTestStatus);
        Assert.True(service.LastConfiguration!.Ntfy.Enabled);
        Assert.Equal("wow-vm-85898-fan2354", service.LastConfiguration.Ntfy.Topic);
    }

    private sealed class RecordingSettingsService(bool failSave, bool requiresConfirmation = false) : ISettingsService
    {
        public int LastCredentialUpdateCount { get; private set; }
        public MonitorConfiguration? LastConfiguration { get; private set; }

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SettingsLoadResult(CreateConfiguration(), requiresConfirmation, []));

        public Task<SettingsSaveResult> SaveAsync(
            SettingsSaveRequest request,
            CancellationToken cancellationToken)
        {
            LastCredentialUpdateCount = request.CredentialUpdates.Count;
            LastConfiguration = request.Configuration;
            return failSave
                ? Task.FromException<SettingsSaveResult>(new IOException("Injected save failure."))
                : Task.FromResult(new SettingsSaveResult(true, []));
        }

        private static MonitorConfiguration CreateConfiguration() =>
            new(
                MonitorConfiguration.CurrentSchemaVersion,
                new MonitoringConfiguration(60, 10, 300, 600),
                [new MachineConfiguration(
                    "vm-01",
                    "VM 01",
                    @"\\server\share",
                    true,
                    "WowVmMonitor/share/vm-01")],
                new NtfyConfiguration(true, "wow-vm-85898-fan2354"));
    }

    private sealed class RecordingNtfyTestService : INtfyTestService
    {
        public string? LastTopic { get; private set; }

        public Task<NtfySendResult> SendTestAsync(string topic, CancellationToken cancellationToken)
        {
            LastTopic = topic;
            return Task.FromResult(NtfySendResult.Success());
        }
    }
}
