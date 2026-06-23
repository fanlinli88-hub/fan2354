namespace WowVmMonitor.Desktop.Lifetime;

public interface IMainWindowHost
{
    void Hide();
    void Show();
    void Restore();
    void Activate();
}
