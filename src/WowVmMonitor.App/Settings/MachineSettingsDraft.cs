using WowVmMonitor.App.Mvvm;
using WowVmMonitor.Core.Configuration;

namespace WowVmMonitor.App.Settings;

public sealed class MachineSettingsDraft : ObservableObject
{
    private string _displayName;
    private string _sharePath;
    private bool _enabled;
    private string _username = string.Empty;
    private char[] _replacementPassword = [];

    public MachineSettingsDraft(MachineConfiguration configuration)
    {
        Id = configuration.Id;
        _displayName = configuration.DisplayName;
        _sharePath = configuration.SharePath;
        _enabled = configuration.Enabled;
    }

    public string Id { get; }
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public string SharePath { get => _sharePath; set => SetProperty(ref _sharePath, value); }
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public string Username { get => _username; set => SetProperty(ref _username, value); }
    public ReadOnlyMemory<char> ReplacementPassword => _replacementPassword;
    public bool HasReplacementPassword => _replacementPassword.Length > 0;

    public void SetReplacementPassword(char[] password)
    {
        ArgumentNullException.ThrowIfNull(password);
        ClearReplacementPassword();
        _replacementPassword = password;
        OnPropertyChanged(nameof(HasReplacementPassword));
    }

    public void ClearReplacementPassword()
    {
        Array.Clear(_replacementPassword);
        _replacementPassword = [];
        OnPropertyChanged(nameof(HasReplacementPassword));
    }

    public MachineConfiguration ToConfiguration() =>
        new(Id, DisplayName, SharePath, Enabled, MachineConfiguration.CredentialTargetFor(Id));

    public override string ToString() =>
        $"MachineSettingsDraft {{ Id = {Id}, Credential = [REDACTED] }}";
}
