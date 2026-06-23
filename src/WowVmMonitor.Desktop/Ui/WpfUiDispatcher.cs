using WowVmMonitor.App.Ui;

namespace WowVmMonitor.Desktop.Ui;

public sealed class WpfUiDispatcher(System.Windows.Threading.Dispatcher dispatcher) : IUiDispatcher
{
    public bool CheckAccess() => dispatcher.CheckAccess();

    public async Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (dispatcher.CheckAccess())
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return;
        }

        await dispatcher.InvokeAsync(action, System.Windows.Threading.DispatcherPriority.DataBind, cancellationToken);
    }
}
