namespace WowVmMonitor.Infrastructure.Credentials;

public sealed class WindowsCredentialStore
{
    public const string DefaultTargetPrefix = "WowVmMonitor/share/";

    private readonly ICredentialNativeApi _nativeApi;
    private readonly string _targetPrefix;

    public WindowsCredentialStore(
        ICredentialNativeApi nativeApi,
        string targetPrefix = DefaultTargetPrefix)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPrefix);
        _nativeApi = nativeApi;
        _targetPrefix = targetPrefix;
    }

    public void Save(string machineId, string username, ReadOnlySpan<char> password)
    {
        var target = GetTarget(machineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        if (password.IsEmpty)
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        if (password.Length > WindowsCredentialNativeApi.MaximumPasswordCharacters)
        {
            throw new ArgumentOutOfRangeException(nameof(password));
        }

        _nativeApi.Write(target, username, password);
    }

    public ShareCredential? Read(string machineId) => _nativeApi.Read(GetTarget(machineId));

    public bool Delete(string machineId) => _nativeApi.Delete(GetTarget(machineId));

    public string GetTarget(string machineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineId);
        if (machineId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new ArgumentException("Machine ID contains unsupported characters.", nameof(machineId));
        }

        return _targetPrefix + machineId;
    }
}
