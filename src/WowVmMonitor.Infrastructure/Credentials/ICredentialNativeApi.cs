namespace WowVmMonitor.Infrastructure.Credentials;

public interface ICredentialNativeApi
{
    void Write(string target, string username, ReadOnlySpan<char> password);

    ShareCredential? Read(string target);

    bool Delete(string target);
}

public sealed class CredentialStoreException : Exception
{
    public CredentialStoreException(string operation, string target, int win32ErrorCode)
        : base($"Credential operation '{operation}' failed for target '{target}' with Win32 error {win32ErrorCode}.")
    {
        Operation = operation;
        Target = target;
        Win32ErrorCode = win32ErrorCode;
    }

    public string Operation { get; }

    public string Target { get; }

    public int Win32ErrorCode { get; }
}
