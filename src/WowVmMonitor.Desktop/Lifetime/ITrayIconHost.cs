namespace WowVmMonitor.Desktop.Lifetime;

public interface ITrayIconHost : IDisposable
{
    void DisableCommands();
}
