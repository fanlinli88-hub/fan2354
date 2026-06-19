namespace WowVmMonitor.Desktop.Lifetime;

public sealed class WpfMainWindowHost(System.Windows.Window window) : IMainWindowHost
{
    public void Hide() => window.Hide();
    public void Show() => window.Show();
    public void Restore() => window.WindowState = System.Windows.WindowState.Normal;
    public void Activate() => window.Activate();
}
