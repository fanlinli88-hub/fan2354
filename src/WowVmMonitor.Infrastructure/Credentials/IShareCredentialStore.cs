namespace WowVmMonitor.Infrastructure.Credentials;

public interface IShareCredentialStore
{
    ShareCredential? Read(string machineId);
}
