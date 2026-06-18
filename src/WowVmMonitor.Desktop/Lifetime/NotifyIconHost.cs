using System.Drawing;
using System.Windows.Forms;

namespace WowVmMonitor.Desktop.Lifetime;

public sealed class NotifyIconHost : ITrayIconHost
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripItem[] _commands;

    public NotifyIconHost(
        Action open,
        Action start,
        Action stop,
        Func<Task> exit)
    {
        var menu = new ContextMenuStrip();
        var openItem = menu.Items.Add("Open", null, (_, _) => open());
        var startItem = menu.Items.Add("Start Monitoring", null, (_, _) => start());
        var stopItem = menu.Items.Add("Stop Monitoring", null, (_, _) => stop());
        var exitItem = menu.Items.Add("Exit", null, (_, _) => _ = RunExitAsync(exit));
        _commands = [openItem, startItem, stopItem, exitItem];
        _icon = new NotifyIcon
        {
            Text = "WowVmMonitor",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => open();
    }

    public void DisableCommands()
    {
        foreach (var command in _commands)
        {
            command.Enabled = false;
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }

    private static async Task RunExitAsync(Func<Task> exit)
    {
        try
        {
            await exit().ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
