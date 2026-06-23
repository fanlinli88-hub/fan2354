using WowVmMonitor.Infrastructure.Credentials;

namespace WowVmMonitor.Tests.Infrastructure;

public sealed class WindowsCredentialStoreTests
{
    [Fact]
    public void EightTargetsRemainIndependent()
    {
        var native = new InMemoryCredentialNativeApi();
        var store = new WindowsCredentialStore(native);

        for (var number = 1; number <= 8; number++)
        {
            store.Save($"vm-{number:D2}", $"user-{number}", $"secret-{number}".ToCharArray());
        }

        using var first = store.Read("vm-01");
        store.Delete("vm-02");
        using var eighth = store.Read("vm-08");

        Assert.Equal("user-1", first!.Username);
        Assert.True(first.Password.Span.SequenceEqual("secret-1".AsSpan()));
        Assert.Null(store.Read("vm-02"));
        Assert.NotNull(eighth);
    }

    [Fact]
    public void DisposingCredentialClearsOwnedPasswordBuffer()
    {
        var credential = new ShareCredential("user", "sensitive-value".ToCharArray());
        var password = credential.Password;

        credential.Dispose();

        Assert.True(password.Span.ToArray().All(character => character == '\0'));
        Assert.DoesNotContain("sensitive-value", credential.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NewStoreInstanceReadsCredentialPersistedByWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var machineId = Guid.NewGuid().ToString("N");
        var first = new WindowsCredentialStore(
            new WindowsCredentialNativeApi(),
            "WowVmMonitor.Tests/share/");
        var second = new WindowsCredentialStore(
            new WindowsCredentialNativeApi(),
            "WowVmMonitor.Tests/share/");

        try
        {
            first.Save(machineId, "restart-user", "restart-secret".ToCharArray());
            using var loaded = second.Read(machineId);

            Assert.NotNull(loaded);
            Assert.Equal("restart-user", loaded.Username);
            Assert.True(loaded.Password.Span.SequenceEqual("restart-secret".AsSpan()));
        }
        finally
        {
            first.Delete(machineId);
        }
    }

    private sealed class InMemoryCredentialNativeApi : ICredentialNativeApi
    {
        private readonly Dictionary<string, (string Username, char[] Password)> _credentials =
            new(StringComparer.Ordinal);

        public void Write(string target, string username, ReadOnlySpan<char> password) =>
            _credentials[target] = (username, password.ToArray());

        public ShareCredential? Read(string target) =>
            _credentials.TryGetValue(target, out var value)
                ? new ShareCredential(value.Username, value.Password.ToArray())
                : null;

        public bool Delete(string target) => _credentials.Remove(target);
    }
}
