namespace WowVmMonitor.Desktop.Lifetime;

public sealed class WpfApplicationShutdown(System.Windows.Application application) : IApplicationShutdown
{
    public void Shutdown()
    {
        if (application.Dispatcher.CheckAccess())
        {
            application.Shutdown();
        }
        else
        {
            application.Dispatcher.BeginInvoke((Action)application.Shutdown);
        }
    }
}
