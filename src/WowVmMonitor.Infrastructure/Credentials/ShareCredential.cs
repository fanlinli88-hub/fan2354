namespace WowVmMonitor.Infrastructure.Credentials;

public sealed class ShareCredential : IDisposable
{
    private readonly char[] _password;
    private bool _disposed;

    public ShareCredential(string username, char[] password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(password);
        Username = username;
        _password = password;
    }

    public string Username { get; }

    public ReadOnlyMemory<char> Password => _password;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Array.Clear(_password);
        _disposed = true;
    }

    public override string ToString() =>
        "ShareCredential { Username = [REDACTED], Password = [REDACTED] }";
}
