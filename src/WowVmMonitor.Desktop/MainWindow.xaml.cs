namespace WowVmMonitor.Desktop;

public partial class MainWindow : System.Windows.Window
{
    public Lifetime.DesktopLifetimeController? LifetimeController { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += OnStateChanged;
        Closing += OnClosing;
    }

    private void OnStateChanged(object? sender, EventArgs eventArgs)
    {
        if (WindowState == System.Windows.WindowState.Minimized)
        {
            LifetimeController?.OnMinimized();
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs eventArgs)
    {
        if (LifetimeController is not null)
        {
            eventArgs.Cancel = LifetimeController.OnWindowClosing();
        }
    }
}
