namespace WowVmMonitor.Infrastructure.Shares;

public interface IWindowsNetworkApi
{
    int Connect(string sharePath, string username, ReadOnlyMemory<char> password);
}
