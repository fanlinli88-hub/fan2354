using WowVmMonitor.Core.Configuration;

namespace WowVmMonitor.App.Settings;

public sealed class CredentialUpdate : IDisposable
{
    private readonly char[] _password;

    public CredentialUpdate(string machineId, string username, char[] password)
    {
        MachineId = machineId;
        Username = username;
        _password = password;
    }

    public string MachineId { get; }
    public string Username { get; }
    public ReadOnlyMemory<char> Password => _password;

    public void Dispose() => Array.Clear(_password);

    public override string ToString() =>
        $"CredentialUpdate {{ MachineId = {MachineId}, Credential = [REDACTED] }}";
}

public sealed class SettingsSaveRequest : IDisposable
{
    public SettingsSaveRequest(
        MonitorConfiguration configuration,
        IReadOnlyList<CredentialUpdate> credentialUpdates)
    {
        Configuration = configuration;
        CredentialUpdates = credentialUpdates;
    }

    public MonitorConfiguration Configuration { get; }
    public IReadOnlyList<CredentialUpdate> CredentialUpdates { get; }

    public void Dispose()
    {
        foreach (var update in CredentialUpdates)
        {
            update.Dispose();
        }
    }
}
