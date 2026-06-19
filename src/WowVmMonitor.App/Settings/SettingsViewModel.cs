using System.Collections.ObjectModel;
using WowVmMonitor.App.Mvvm;
using WowVmMonitor.App.Notifications;
using WowVmMonitor.Core.Configuration;
using WowVmMonitor.Core.Monitoring;

namespace WowVmMonitor.App.Settings;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _service;
    private readonly INtfyTestService _ntfyTestService;
    private int _checkIntervalSeconds;
    private int _checkTimeoutSeconds;
    private int _warningAfterSeconds;
    private int _alertAfterSeconds;
    private string? _statusMessage;
    private bool _requiresUserConfirmation;
    private bool _ntfyEnabled;
    private string _ntfyTopic;
    private string? _ntfyTestStatus;

    private SettingsViewModel(
        ISettingsService service,
        SettingsLoadResult loadResult,
        INtfyTestService ntfyTestService)
    {
        _service = service;
        _ntfyTestService = ntfyTestService;
        var monitoring = loadResult.Configuration.Monitoring;
        _checkIntervalSeconds = monitoring.CheckIntervalSeconds;
        _checkTimeoutSeconds = monitoring.CheckTimeoutSeconds;
        _warningAfterSeconds = monitoring.WarningAfterSeconds;
        _alertAfterSeconds = monitoring.AlertAfterSeconds;
        _ntfyEnabled = loadResult.Configuration.Ntfy.Enabled;
        _ntfyTopic = loadResult.Configuration.Ntfy.Topic;
        foreach (var machine in loadResult.Configuration.Machines)
        {
            Machines.Add(new MachineSettingsDraft(machine));
        }

        _requiresUserConfirmation = loadResult.RequiresUserConfirmation;
        SaveCommand = new AsyncCommand(SaveAsync);
        TestNtfyCommand = new AsyncCommand(TestNtfyAsync);
        AddMachineCommand = new RelayCommand(_ => AddMachine(), _ => Machines.Count < MultiVmMonitor.MaximumMachineCount);
        RemoveMachineCommand = new RelayCommand(RemoveMachine, parameter => parameter is MachineSettingsDraft);
    }

    public ObservableCollection<MachineSettingsDraft> Machines { get; } = [];
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand TestNtfyCommand { get; }
    public RelayCommand AddMachineCommand { get; }
    public RelayCommand RemoveMachineCommand { get; }
    public bool RequiresUserConfirmation
    {
        get => _requiresUserConfirmation;
        private set => SetProperty(ref _requiresUserConfirmation, value);
    }
    public int CheckIntervalSeconds { get => _checkIntervalSeconds; set => SetProperty(ref _checkIntervalSeconds, value); }
    public int CheckTimeoutSeconds { get => _checkTimeoutSeconds; set => SetProperty(ref _checkTimeoutSeconds, value); }
    public int WarningAfterSeconds { get => _warningAfterSeconds; set => SetProperty(ref _warningAfterSeconds, value); }
    public int AlertAfterSeconds { get => _alertAfterSeconds; set => SetProperty(ref _alertAfterSeconds, value); }
    public string? StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool NtfyEnabled { get => _ntfyEnabled; set => SetProperty(ref _ntfyEnabled, value); }
    public string NtfyTopic { get => _ntfyTopic; set => SetProperty(ref _ntfyTopic, value); }
    public string? NtfyTestStatus { get => _ntfyTestStatus; private set => SetProperty(ref _ntfyTestStatus, value); }

    public static async Task<SettingsViewModel> CreateAsync(
        ISettingsService service,
        CancellationToken cancellationToken,
        INtfyTestService? ntfyTestService = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        var loadResult = await service.LoadAsync(cancellationToken);
        return new SettingsViewModel(service, loadResult, ntfyTestService ?? new NullNtfyTestService());
    }

    public override string ToString() =>
        $"SettingsViewModel {{ Machines = {Machines.Count}, Credentials = [REDACTED] }}";

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var configuration = new MonitorConfiguration(
            MonitorConfiguration.CurrentSchemaVersion,
            new MonitoringConfiguration(
                CheckIntervalSeconds,
                CheckTimeoutSeconds,
                WarningAfterSeconds,
                AlertAfterSeconds),
            Machines.Select(machine => machine.ToConfiguration()).ToArray(),
            new NtfyConfiguration(NtfyEnabled, NtfyTopic));
        var validationErrors = ConfigurationValidator.Validate(configuration);
        if (validationErrors.Count > 0)
        {
            StatusMessage = validationErrors[0].Message;
            ClearPasswords();
            return;
        }

        var updates = Machines
            .Where(machine => machine.HasReplacementPassword)
            .Select(machine => new CredentialUpdate(
                machine.Id,
                machine.Username,
                machine.ReplacementPassword.ToArray()))
            .ToArray();

        using var request = new SettingsSaveRequest(configuration, updates);
        try
        {
            var result = await _service.SaveAsync(request, cancellationToken);
            StatusMessage = result.Succeeded ? "设置已保存。" : "设置保存失败。";
            RequiresUserConfirmation = !result.Succeeded;
        }
        finally
        {
            ClearPasswords();
        }
    }

    private async Task TestNtfyAsync(CancellationToken cancellationToken)
    {
        if (!NtfyConfiguration.IsValidTopic(NtfyTopic))
        {
            NtfyTestStatus = "ntfy 主题无效。";
            return;
        }

        var result = await _ntfyTestService.SendTestAsync(NtfyTopic, cancellationToken);
        NtfyTestStatus = result.Succeeded
            ? "测试通知发送成功。"
            : "测试通知发送失败，请检查网络和主题。";
    }

    private void ClearPasswords()
    {
        foreach (var machine in Machines)
        {
            machine.ClearReplacementPassword();
        }
    }

    private void AddMachine()
    {
        var id = Enumerable.Range(1, MultiVmMonitor.MaximumMachineCount)
            .Select(number => $"machine-{number}")
            .First(candidate => Machines.All(machine => !machine.Id.Equals(candidate, StringComparison.OrdinalIgnoreCase)));
        Machines.Add(new MachineSettingsDraft(new MachineConfiguration(
            id, $"机器 {Machines.Count + 1}", @"\\服务器\共享目录", false,
            MachineConfiguration.CredentialTargetFor(id))));
        AddMachineCommand.RaiseCanExecuteChanged();
    }

    private void RemoveMachine(object? parameter)
    {
        if (parameter is MachineSettingsDraft machine)
        {
            machine.ClearReplacementPassword();
            Machines.Remove(machine);
            AddMachineCommand.RaiseCanExecuteChanged();
        }
    }
}
